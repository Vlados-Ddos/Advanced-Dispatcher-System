using AdvancedDispatcherSystem.Core;
using System.Collections.Concurrent;
using System.Threading.Channels;
using System.Net;
using System.Net.Sockets;

namespace AdvancedDispatcherSystem.Host;

public sealed class GameConnection
{
    private readonly string secret;
    private readonly StateHub hub;
    private readonly int ipcPort;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<CommandResult>> pending = new();
    private Channel<WireFrame> outgoing;
    private readonly object sync = new();
    public GameConnection(string secret, StateHub hub, int ipcPort) { this.secret = secret; this.hub = hub; this.ipcPort = ipcPort; }
    public Task Run(CancellationToken stop) => RunTcp(stop);
    private async Task RunTcp(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            using var client = new TcpClient(AddressFamily.InterNetwork) { NoDelay = true };
            using var connection = CancellationTokenSource.CreateLinkedTokenSource(stop);
            Channel<WireFrame> queue = null;
            try
            {
                using (var auth = CancellationTokenSource.CreateLinkedTokenSource(stop))
                {
                    auth.CancelAfter(5000); await client.ConnectAsync(IPAddress.Loopback, ipcPort, auth.Token);
                    var challenge = Json.Read<WireFrame>(await Framing.Read(client.GetStream(), auth.Token, 4096));
                    if (challenge == null || challenge.protocol != Protocol.Version || challenge.kind != "challenge" || challenge.token == null || challenge.token.Length != 64 || !IpcAuth.Equal(challenge.proof, IpcAuth.Proof(secret, "game", challenge.token))) throw new IOException("IPC_AUTH");
                    string nonce = IpcAuth.Nonce();
                    await Framing.Write(client.GetStream(), Json.Bytes(new WireFrame { kind = "hello", token = nonce, proof = IpcAuth.Proof(secret, "host", challenge.token + ":" + nonce) }), auth.Token);
                    var ready = Json.Read<WireFrame>(await Framing.Read(client.GetStream(), auth.Token, 4096));
                    if (ready == null || ready.kind != "ready" || ready.protocol != Protocol.Version) throw new IOException("IPC_PROTOCOL");
                }
                queue = Channel.CreateBounded<WireFrame>(64); lock (sync) outgoing = queue; hub.GameConnected = true;
                var stream = client.GetStream(); var writer = WriteLoop(stream, queue, connection.Token, client);
                try
                {
                    while (!stop.IsCancellationRequested)
                    {
                        byte[] bytes = await Framing.Read(stream, connection.Token);
                        if (MotionCodec.IsMotion(bytes)) { hub.Apply(MotionCodec.Read(bytes)); continue; }
                        var frame = Json.Read<WireFrame>(bytes);
                        if (frame == null || frame.protocol != Protocol.Version) throw new IOException("PROTOCOL_VERSION");
                        if (frame.kind == "topology" && frame.topology != null) hub.SetTopology(frame.topology);
                        else if (frame.kind == "state" && frame.batch != null) hub.Apply(frame.batch);
                        else if (frame.kind == "result" && frame.result != null && pending.TryRemove(frame.result.id, out var waiter)) waiter.TrySetResult(frame.result);
                    }
                }
                finally { connection.Cancel(); client.Dispose(); try { await writer; } catch (Exception e) when (e is IOException || e is InvalidDataException || e is SocketException || e is OperationCanceledException || e is ObjectDisposedException) { /* Connection already closing. */ } }
            }
            catch (Exception e) when (e is IOException || e is InvalidDataException || e is SocketException || e is OperationCanceledException || e is System.Text.Json.JsonException || e is ArgumentException)
            { if (!stop.IsCancellationRequested) Console.Error.WriteLine("IPC_RECONNECT " + e.GetType().Name); }
            finally
            {
                lock (sync) outgoing = null; queue?.Writer.TryComplete(); hub.Disconnected();
                foreach (var item in pending) if (pending.TryRemove(item.Key, out var waiter)) waiter.TrySetResult(new CommandResult { id = item.Key, status = "outcomeUnknown", code = "GAME_DISCONNECTED" });
            }
            try { await Task.Delay(1000, stop); } catch (OperationCanceledException) { }
        }
    }
    private static async Task WriteLoop(Stream stream, Channel<WireFrame> channel, CancellationToken stop, TcpClient client)
    {
        try {
            await foreach (var frame in channel.Reader.ReadAllAsync(stop))
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop); deadline.CancelAfter(5000);
                await Framing.Write(stream, Json.Bytes(frame), deadline.Token);
            }
        }
        finally { client.Dispose(); } // Wake the reader when a write fails or times out.
    }
    public async Task<CommandResult> Execute(Command command)
    {
        var waiter = new TaskCompletionSource<CommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (pending.Count >= 64 || !pending.TryAdd(command.id, waiter)) return new CommandResult { id = command.id, status = "rejected", code = "QUEUE_FULL" };
        int timeout = command.kind == "setTurntable" ? 45000 : 8000;
        command.deadline = Protocol.Now + timeout;
        bool sent;
        lock (sync) sent = outgoing != null && outgoing.Writer.TryWrite(new WireFrame { kind = "command", command = command });
        if (!sent) { pending.TryRemove(command.id, out _); return new CommandResult { id = command.id, status = "rejected", code = "GAME_DISCONNECTED" }; }
        try { return await waiter.Task.WaitAsync(TimeSpan.FromMilliseconds(timeout + 2000)); }
        catch (TimeoutException) { return new CommandResult { id = command.id, status = "outcomeUnknown", code = "COMMAND_TIMEOUT" }; }
        finally { pending.TryRemove(command.id, out _); }
    }
}
