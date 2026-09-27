using System.Net.WebSockets;
using System.Text.Json;
using AdvancedDispatcherSystem.Core;

namespace AdvancedDispatcherSystem.Host;

public static class BrowserSocket
{
    public static async Task Handle(HttpContext ctx, StateHub hub, Accounts accounts, CommandGateway gateway, RateGate rates)
    {
        string token = ctx.Request.Cookies["ads_session"];
        var auth = accounts.Get(token);
        if (auth == null) { ctx.Response.StatusCode = 401; return; }
        if (!ctx.WebSockets.IsWebSocketRequest || string.IsNullOrEmpty(ctx.Request.Headers.Origin)) { ctx.Response.StatusCode = 400; return; }
        if (!rates.Allow("ws:" + auth.name, 100, 60000)) { ctx.Response.StatusCode = 429; return; }
        long.TryParse(ctx.Request.Query["seq"], out long lastSeq);
        var peer = hub.Connect(ctx.Request.Query["epoch"], lastSeq);
        if (peer == null) { ctx.Response.StatusCode = 503; return; }
        WebSocket socket = null;
        Task sender = Task.CompletedTask;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ctx.RequestAborted, peer.Cancel.Token);
        try
        {
            socket = await ctx.WebSockets.AcceptWebSocketAsync();
            sender = Send(socket, peer, accounts, token, stop.Token);
            var buffer = new byte[Protocol.MaxCommandBytes];
            int inFlight = 0;
            var connectionStop = stop.Token;
            void Receipt(CommandResult result)
            {
                if (connectionStop.IsCancellationRequested) return;
                try { peer.Send(Json.Bytes(new { type = "receipt", payload = result })); }
                catch (ObjectDisposedException) { /* A command can finish after this socket closed. */ }
            }
            async Task Finish(Task<CommandResult> work, string id)
            {
                try { Receipt(await work); }
                catch (Exception e) {
                    Console.Error.WriteLine("WS_COMMAND_FAILED " + e.GetType().Name);
                    Receipt(new CommandResult { id=id, status="outcomeUnknown", code="COMMAND_FAILED" });
                }
                finally { Interlocked.Decrement(ref inFlight); }
            }
            while (!stop.IsCancellationRequested)
            {
                int used = 0; WebSocketReceiveResult received;
                do
                {
                    if (used == buffer.Length) throw new InvalidDataException("COMMAND_SIZE");
                    received = await socket.ReceiveAsync(new ArraySegment<byte>(buffer, used, buffer.Length - used), stop.Token);
                    if (received.MessageType != WebSocketMessageType.Text) return;
                    used += received.Count;
                } while (!received.EndOfMessage);
                var user = accounts.Get(token); if (user == null) return;
                var command = JsonSerializer.Deserialize<Command>(buffer.AsSpan(0, used), Json.Options);
                // Keep receiving while a route/turntable command waits for Unity. In
                // particular, cancellation must reach the gateway on this same socket.
                if (Volatile.Read(ref inFlight) >= (command?.kind == "cancelRoute" ? 64 : 16)) {
                    Receipt(new CommandResult { id=command?.id ?? "", status="rejected", code="QUEUE_FULL" }); continue;
                }
                var work = gateway.Run(command, user);
                if (!work.IsCompleted) Receipt(new CommandResult { id=command?.id, status="accepted" });
                Interlocked.Increment(ref inFlight);
                _ = Finish(work, command?.id ?? "");
            }
        }
        catch (Exception e) when (e is OperationCanceledException || e is WebSocketException || e is IOException || e is InvalidDataException || e is JsonException)
        {
            if (e is JsonException || e is InvalidDataException) Console.Error.WriteLine("WS_PROTOCOL_REJECTED " + e.GetType().Name);
            // Cancellation and socket closure are normal disconnect paths.
        }
        catch (InvalidOperationException e) when (socket == null)
        {
            Console.Error.WriteLine("WS_UPGRADE_REJECTED " + e.GetType().Name);
            if (!ctx.Response.HasStarted) ctx.Response.StatusCode = 503;
        }
        finally
        {
            stop.Cancel(); socket?.Abort();
            // Release the peer slot before waiting for a sender that may still
            // be draining a bounded queue. Rapid reconnects must not observe
            // already-closed sockets as active clients.
            hub.Remove(peer);
            try { await sender; }
            catch (Exception e) when (e is OperationCanceledException || e is WebSocketException || e is IOException) { /* The receive side already closed the connection. */ }
            finally { socket?.Dispose(); peer.Cancel.Dispose(); }
        }
    }
    private static async Task Send(WebSocket socket, BrowserPeer peer, Accounts accounts, string token, CancellationToken stop)
    {
        try
        {
            await foreach (var bytes in peer.Outbox.Reader.ReadAllAsync(stop))
            {
                if (accounts.Get(token) == null) return;
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop); timeout.CancelAfter(5000);
                await socket.SendAsync(bytes, bytes[0] == 31 ? WebSocketMessageType.Binary : WebSocketMessageType.Text, true, timeout.Token);
                peer.Sent(bytes.Length);
            }
        }
        finally { peer.Cancel.Cancel(); }
    }
}
