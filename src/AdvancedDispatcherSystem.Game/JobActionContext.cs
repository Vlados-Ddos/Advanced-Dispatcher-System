using System;

namespace AdvancedDispatcherSystem.Game
{
    // Scoped to the synchronous native action, never to the owner/booklet holder.
    // Finalizers restore nesting even if the game rejects or throws.
    public sealed class JobActionContext : IDisposable
    {
        [ThreadStatic] private static JobActionContext current;
        public static JobActionContext Current => current;
        public readonly string PlayerId, PlayerName;
        private readonly JobActionContext previous;
        private bool disposed;
        public JobActionContext(string id, string name)
        { PlayerId = id; PlayerName = name; previous = current; current = this; }
        public void Dispose() { if (disposed) return; disposed = true; current = previous; }
    }
}
