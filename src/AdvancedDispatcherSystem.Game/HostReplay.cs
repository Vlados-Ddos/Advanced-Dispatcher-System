using System.Collections.Generic;
using AdvancedDispatcherSystem.Core;

namespace AdvancedDispatcherSystem.Game
{
    public sealed partial class Dispatcher
    {
        private void ReplayIfRequested()
        {
            if (Main.Bridge == null || !Main.Bridge.Connected || !Main.Bridge.TakeResync()) return;
            // IPC reconnect/backpressure asks for another snapshot. Native route
            // ownership belongs to the running world and survives Host restart.
            Replay();
        }

        private void Replay()
        {
            Main.Bridge.Send(new WireFrame { kind = "topology", topology = topology });
            Main.Bridge.Send(new WireFrame
            {
                kind = "state",
                batch = new GameBatch
                {
                    epoch = epoch, topologyRevision = topologyRevision,
                    reset = true, events = TakeEvents(), routeStates = RouteStates(),
                    switches = Values(switchStates), turntables = CaptureTables(true),
                    cars = Values(carStates), signals = Values(signalStates),
                    blocks = Values(blockStates), occupancy = Values(occupancyStates),
                    players = CapturePlayers(), replacePlayers = true,
                    signs = Values(signStates), replaceSigns = true,
                    jobs = lastJobs, locations = lastLocations, replaceLocations = true,
                    replaceJobs = true, capabilities = Capabilities()
                }
            });
            ClearChanges();
        }
        private static T[] Values<T>(Dictionary<string, T> map)
        {
            var result = new T[map.Count]; map.Values.CopyTo(result, 0); return result;
        }
    }
}
