using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AdvancedDispatcherSystem.Core;
using Newtonsoft.Json;

namespace AdvancedDispatcherSystem.Game
{
    public sealed class IpcBridge : IDisposable
    {
        private static readonly object workerGate = new object();
        private static Task previousWorker = Task.CompletedTask;
        private int started, disposed, restartAttempts;
        private sealed class Work { public WireFrame frame; public Action release; }
        private sealed class MetadataResolver : Newtonsoft.Json.Serialization.DefaultContractResolver
        {
            protected override Newtonsoft.Json.Serialization.JsonProperty CreateProperty(System.Reflection.MemberInfo member, MemberSerialization serialization)
            { var property = base.CreateProperty(member, serialization); if (member.DeclaringType == typeof(GameBatch) && member.Name == "motions") property.Ignored = true; return property; }
        }
        private readonly string directory, secret;
        private readonly int webPort;
        private readonly bool lan, https;
        private string auditedTopology;
        private readonly CancellationTokenSource stop = new CancellationTokenSource();
        private readonly BlockingCollection<Work> output = new BlockingCollection<Work>(64);
        private readonly ConcurrentQueue<Command> commands = new ConcurrentQueue<Command>();
        private readonly MemoryStream encoded = new MemoryStream(65536);
        private readonly JsonSerializer serializer = JsonSerializer.Create(new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore, TypeNameHandling = TypeNameHandling.None, ContractResolver = new MetadataResolver() });
        private volatile TcpClient current;
        private TcpListener listener;
        private Process process;
        private int commandCount, resync = 1;
        private volatile bool connected;
        public volatile string LastError = "";
        public bool Connected => connected;
        public string Address { get; private set; }
        public IpcBridge(string directory, Settings config, string secret)
        { this.directory = directory; this.secret = secret; webPort = config.Port; lan = config.Lan; https = config.Https; Address = (https ? "https" : "http") + "://127.0.0.1:" + webPort; }
        public void Start()
        {
            if (Interlocked.Exchange(ref started, 1) != 0) return;
            lock (workerGate) {
                var previous = previousWorker;
                previousWorker = Task.Run(async () => {
                    try { await previous.ConfigureAwait(false); }
                    catch (Exception e) { LastError = "PREVIOUS_BRIDGE_FAULT " + e.GetType().Name; }
                    await Run().ConfigureAwait(false);
                });
            }
        }
        private void Launch(int port)
        {
            string exe = Path.Combine(directory, "Host", "AdvancedDispatcherSystem.Host.exe");
            var info = new ProcessStartInfo(exe)
            {
                WorkingDirectory = Path.GetDirectoryName(exe),
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                Arguments = "--port " + webPort + " --lan " + lan.ToString().ToLowerInvariant() + " --https " + https.ToString().ToLowerInvariant() + " --ipc-port " + port + " --parent " + Process.GetCurrentProcess().Id
            };
            info.EnvironmentVariables["ADS_IPC_TOKEN"] = secret; process = Process.Start(info);
            process.ErrorDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) LastError = e.Data.Length > 240 ? e.Data.Substring(0, 240) : e.Data; }; process.OutputDataReceived += (s, e) => { }; process.BeginErrorReadLine(); process.BeginOutputReadLine();
        }
        public bool TakeResync() => Interlocked.Exchange(ref resync, 0) != 0;
        public bool Send(WireFrame frame, Action release = null)
        {
            try { if (!stop.IsCancellationRequested && output.TryAdd(new Work { frame = frame, release = release })) return true; } catch (InvalidOperationException) { /* Dispose closed the bounded queue. */ }
            release?.Invoke(); Interlocked.Exchange(ref resync, 1); return false;
        }
        public bool TryCommand(out Command command) { if (!commands.TryDequeue(out command)) return false; Interlocked.Decrement(ref commandCount); return true; }
        private async Task<TcpClient> AcceptHost()
        {
            var accept = listener.AcceptTcpClientAsync();
            try {
                while (!accept.IsCompleted) {
                    stop.Token.ThrowIfCancellationRequested();
                    if (process == null || process.HasExited) {
                        KillHost();
                        LastError = "WEB_HOST_RESTARTING";
                        await Task.Delay(Math.Min(30000, 1000 << Math.Min(5, restartAttempts++)), stop.Token).ConfigureAwait(false);
                        try { Launch(((IPEndPoint)listener.LocalEndpoint).Port); }
                        catch (Exception e) when (e is IOException || e is System.ComponentModel.Win32Exception) { LastError = "WEB_HOST_RESTART_FAILED " + e.GetType().Name; }
                    }
                    await Task.WhenAny(accept, Task.Delay(500, stop.Token)).ConfigureAwait(false);
                }
                return await accept.ConfigureAwait(false);
            }
            catch {
                // Stop the pending accept and observe it before releasing this worker.
                listener.Stop();
                try { var abandoned = await accept.ConfigureAwait(false); abandoned.Close(); }
                catch (Exception e) when (e is SocketException || e is ObjectDisposedException) { }
                throw;
            }
        }
        private async Task Run()
        {
            try
            {
                if (stop.IsCancellationRequested) return;
                // Reserve the ephemeral loopback port before launching the authenticated client.
                listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(4); if (stop.IsCancellationRequested) return; Launch(((IPEndPoint)listener.LocalEndpoint).Port);
                while (!stop.IsCancellationRequested)
                {
                    using (var client = await AcceptHost().ConfigureAwait(false))
                    using (var connection = CancellationTokenSource.CreateLinkedTokenSource(stop.Token))
                    {
                        current = client; client.NoDelay = true;
                        try
                        {
                            var stream = client.GetStream();
                            using (var auth = CancellationTokenSource.CreateLinkedTokenSource(stop.Token))
                            using (auth.Token.Register(() => client.Close()))
                            {
                                auth.CancelAfter(5000); string nonce = IpcAuth.Nonce();
                                await Write(stream, new WireFrame { kind = "challenge", token = nonce, proof = IpcAuth.Proof(secret, "game", nonce) }, auth.Token).ConfigureAwait(false);
                                var hello = JsonConvert.DeserializeObject<WireFrame>(Encoding.UTF8.GetString(await Framing.Read(stream, auth.Token, 4096).ConfigureAwait(false)));
                                if (hello == null || hello.protocol != Protocol.Version || hello.kind != "hello" || hello.token == null || hello.token.Length != 64 || !IpcAuth.Equal(hello.proof, IpcAuth.Proof(secret, "host", nonce + ":" + hello.token))) throw new IOException("IPC_AUTH");
                                await Write(stream, new WireFrame { kind = "ready" }, auth.Token).ConfigureAwait(false);
                            }
                            while (output.TryTake(out var discarded)) discarded.release?.Invoke();
                            connected = true; restartAttempts = 0; Interlocked.Exchange(ref resync, 1); LastError = "";
                            var writer = Task.Run(async () =>
                            {
                                try { foreach (var work in output.GetConsumingEnumerable(connection.Token)) { try { await Write(stream, work.frame, connection.Token).ConfigureAwait(false); } finally { work.release?.Invoke(); } } }
                                catch (Exception e) { if (!stop.IsCancellationRequested) LastError = "IPC_WRITE_FAILED " + e.GetType().Name; connection.Cancel(); client.Close(); throw; }
                            });
                            try
                            {
                                while (!stop.IsCancellationRequested)
                                {
                                    var frame = JsonConvert.DeserializeObject<WireFrame>(Encoding.UTF8.GetString(await Framing.Read(stream, connection.Token, Protocol.MaxInternalCommandBytes).ConfigureAwait(false)));
                                    if (frame == null || frame.protocol != Protocol.Version || frame.kind != "command" || frame.command == null || string.IsNullOrWhiteSpace(frame.command.id)) throw new IOException("IPC_PROTOCOL");
                                    if (Interlocked.Increment(ref commandCount) > 64) { Interlocked.Decrement(ref commandCount); Send(new WireFrame { kind = "result", result = new CommandResult { id = frame.command.id, status = "rejected", code = "QUEUE_FULL" } }); } else commands.Enqueue(frame.command);
                                }
                            }
                            finally { connection.Cancel(); client.Close(); try { await writer.ConfigureAwait(false); } catch (Exception e) { if (!stop.IsCancellationRequested) LastError = "IPC_WRITE_FAILED " + e.GetType().Name; } }
                        }
                        catch (Exception e) { if (!stop.IsCancellationRequested) LastError = "IPC_RECONNECT " + e.GetType().Name; }
                        finally { connected = false; current = null; while (commands.TryDequeue(out var cancelled)) Interlocked.Decrement(ref commandCount); }
                    }
                }
            }
            catch (Exception e) { if (!stop.IsCancellationRequested) LastError = "WEB_HOST_FAILED " + e.Message; }
            finally { connected = false; listener?.Stop(); while (output.TryTake(out var discarded)) discarded.release?.Invoke(); KillHost(); encoded.Dispose(); }
        }
        private async Task Write(Stream stream, WireFrame frame, CancellationToken cancel)
        {
            encoded.SetLength(0);
            using (var writer = new StreamWriter(encoded, new UTF8Encoding(false), 4096, true)) using (var json = new JsonTextWriter(writer) { CloseOutput = false }) { serializer.Serialize(json, frame); json.Flush(); writer.Flush(); }
            await Framing.Write(stream, encoded.GetBuffer(), (int)encoded.Length, cancel).ConfigureAwait(false);
            // Explicit local QA opt-in. Reuse the already serialized topology on
            // the IPC worker; never scan Unity or export accounts/player data.
            if (frame.kind == "topology" && frame.topology != null)
            {
                string key = frame.topology.epoch + ":" + frame.topology.revision;
                if (key != auditedTopology && File.Exists(Path.Combine(directory, "capture-topology.enabled")))
                {
                    try {
                        string audit = Path.Combine(directory, "Diagnostics"); Directory.CreateDirectory(audit);
                        string target = Path.Combine(audit, "topology-" + frame.topology.epoch + "-" + frame.topology.revision + ".json");
                        using (var file = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.Read)) file.Write(encoded.GetBuffer(), 0, (int)encoded.Length);
                        auditedTopology = key;
                    } catch (Exception e) { LastError = "TOPOLOGY_AUDIT_FAILED " + e.GetType().Name; }
                }
            }
            if (frame.batch?.motions != null && frame.batch.motions.Count > 0) { encoded.SetLength(0); MotionCodec.Write(encoded, frame.batch.epoch, frame.batch.topologyRevision, frame.batch.motions); await Framing.Write(stream, encoded.GetBuffer(), (int)encoded.Length, cancel).ConfigureAwait(false); }
        }
        private void KillHost()
        {
            var owned = Interlocked.Exchange(ref process, null); if (owned == null) return;
            try { if (!owned.HasExited) { owned.Kill(); if (!owned.WaitForExit(3000)) LastError = "WEB_HOST_SHUTDOWN_TIMEOUT"; } }
            catch (InvalidOperationException) { /* Process exited between HasExited and Kill. */ }
            catch (System.ComponentModel.Win32Exception e) { LastError = "WEB_HOST_SHUTDOWN_FAILED " + e.NativeErrorCode; }
            finally { owned.Dispose(); }
        }
        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            stop.Cancel(); current?.Close(); listener?.Stop(); output.CompleteAdding();
            // Run's finally owns process teardown; Start waits for that worker before replacing it.
        }
    }
}
