using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using AdvancedDispatcherSystem.Core;

namespace AdvancedDispatcherSystem.Game
{
    public sealed partial class Dispatcher
    {
        private readonly ConcurrentQueue<EventEntry> gameEvents = new ConcurrentQueue<EventEntry>();
        private int gameEventCount;
        internal void ReportEvent(string code, string target, string detail, string type, string severity = "info", string actor = null, string actorId = null)
        {
            if (!world) return;
            if (Interlocked.Increment(ref gameEventCount) > 128) { Interlocked.Decrement(ref gameEventCount); return; }
            gameEvents.Enqueue(new EventEntry { code = code, actor = actor, actorId = actorId, target = target, detail = detail, type = type, severity = severity, source = "game", time = Protocol.Now });
        }
        private EventEntry[] TakeEvents()
        {
            var list = new List<EventEntry>();
            while (list.Count < 64 && gameEvents.TryDequeue(out var e)) { Interlocked.Decrement(ref gameEventCount); list.Add(e); }
            return list.Count == 0 ? null : list.ToArray();
        }
        private void ClearEvents() { while (gameEvents.TryDequeue(out var ignored)) Interlocked.Decrement(ref gameEventCount); }
    }
}
