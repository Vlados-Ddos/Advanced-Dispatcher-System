using System;
using System.Collections.Generic;
using AdvancedDispatcherSystem.Core;
using HarmonyLib;
using UnityEngine;

namespace AdvancedDispatcherSystem.Game
{
    public sealed partial class Dispatcher
    {
        private sealed class TableEntry
        {
            public TurntableRailTrack table;
            public TurntableController controller;
            public TurntableDef def;
            public TurntableState state;
            public Command command;
            public float lastAngle, drivenAngle, goal;
        }
        private readonly Dictionary<string, TableEntry> tables = new Dictionary<string, TableEntry>();
        private readonly HashSet<RailTrack> tableTracks = new HashSet<RailTrack>();
        private readonly List<CarState> tableSafetyCars = new List<CarState>();
        private static readonly System.Reflection.FieldInfo tableLever = AccessTools.Field(typeof(TurntableController), "leverControl");
        private static readonly System.Reflection.FieldInfo tablePushPositive = AccessTools.Field(typeof(TurntableController), "pushingPositiveDirectionValue");
        private static readonly System.Reflection.FieldInfo tablePushNegative = AccessTools.Field(typeof(TurntableController), "pushingNegativeDirectionValue");

        private TurntableDef[] CaptureTableDefinitions()
        {
            ResetTables("TOPOLOGY_CHANGED");
            var definitions = new List<TurntableDef>();
            var controllers = Resources.FindObjectsOfTypeAll<TurntableController>();
            foreach (var table in UnityEngine.Object.FindObjectsOfType<TurntableRailTrack>())
            {
                var track = TrackId(table.Track); if (track == null) continue;
                var position = table.transform.position - WorldMover.currentMove;
                var ends = new List<TurntableEnd>();
                foreach (var end in table.trackEnds)
                    if (end?.track != null && TrackId(end.track) is string id)
                        ends.Add(new TurntableEnd { track = id, end = end.isFirst ? 0 : 1, angle = end.angle });
                var def = new TurntableDef { id = "tt:" + track, name = "Turntable " + table.uniqueID, track = track, x = position.x, z = position.z, radius = table.SearchRadius, ends = ends.ToArray() };
                var controller = Array.Find(controllers,candidate=>candidate.turntable==table);
                tables.Add(def.id, new TableEntry { table = table, controller = controller, def = def, lastAngle = table.currentYRotation });
                tableTracks.Add(table.Track); definitions.Add(def);
            }
            return definitions.ToArray();
        }
        private void ResetTables(string code)
        {
            foreach (var entry in tables.Values) if (entry.command != null) FinishTable(entry, code);
            tables.Clear(); tableTracks.Clear();
            tableSafetyCars.Clear();
        }
        private string TableSafety(TableEntry entry, bool fresh)
        {
            if (entry.table == null || entry.controller == null || entry.table.visuals == null) return "TURNTABLE_AREA_UNLOADED";
            if (entry.table.trackEnds.Count != entry.def.ends.Length) return "TOPOLOGY_CHANGED";
            for (int i = 0; i < entry.def.ends.Length; i++)
            {
                var actual = entry.table.trackEnds[i]; var expected = entry.def.ends[i];
                if (actual == null || TrackId(actual.track) != expected.track || actual.isFirst != (expected.end == 0) || Math.Abs(TurntableRules.Difference(actual.angle, expected.angle)) > .01) return "TOPOLOGY_CHANGED";
            }
            if (!entry.controller.PlayerControlAllowed || tableLever == null || tablePushPositive == null || tablePushNegative == null) return "TURNTABLE_BUSY";
            var lever = tableLever.GetValue(entry.controller) as DV.CabControls.LeverBase;
            if (lever == null && entry.controller.leverGO != null) lever = entry.controller.leverGO.GetComponent<DV.CabControls.LeverBase>();
            if (lever == null) return "TURNTABLE_CONTROL_UNAVAILABLE";
            if (Mathf.Abs(lever.Value - .5f) > .05f || entry.controller.isActiveAndEnabled && ((float)tablePushPositive.GetValue(entry.controller) != 0 || (float)tablePushNegative.GetValue(entry.controller) != 0)) return "TURNTABLE_BUSY";
            if (!fresh) return TurntableRules.Safety(entry.def.radius, carStates.Values, entry.def.x, entry.def.z, entry.def.track);
            // Re-read every live registered car, including hidden/undiscovered cars.
            var observed = tableSafetyCars; observed.Clear();
            foreach (var item in carList)
            {
                var car = item.car; if (car == null) continue;
                var p = car.transform.TransformPoint(car.Bounds.center) - WorldMover.currentMove;
                // TrainCar.Bounds is local collision geometry (the game's
                // placement code projects Bounds.center.z along transform.forward).
                // Transform the centre once and keep its local X width.
                var width = car.Bounds.size.x;
                observed.Add(new CarState { x = p.x, z = p.z, yaw = car.transform.eulerAngles.y, width = width, speed = car.GetForwardSpeed() * 3.6, length = car.InterCouplerDistance, track1 = TrackId(car.FrontBogie?.track), track2 = TrackId(car.RearBogie?.track) });
            }
            return TurntableRules.Safety(entry.def.radius, observed, entry.def.x, entry.def.z, entry.def.track);
        }
        private TurntableState[] CaptureTables(bool full = false)
        {
            var values = new List<TurntableState>();
            foreach (var entry in tables.Values)
            {
                var table = entry.table; if (table == null) { RequestRebuild(); continue; }
                var set = table.Track.GetKinkedPointSet();
                var points = new double[set.points.Length > 1 ? 4 : 0];
                if (points.Length == 4) { var a = set.points[0].position; var b = set.points[set.points.Length - 1].position; points[0] = a.x; points[1] = a.z; points[2] = b.x; points[3] = b.z; }
                string reason = TableSafety(entry, false);
                if (reason == "TOPOLOGY_CHANGED") RequestRebuild();
                bool moving = entry.command != null || Math.Abs(TurntableRules.Difference(table.currentYRotation, entry.lastAngle)) > .001;
                var previous = entry.state;
                bool changed = previous == null || previous.angle != table.currentYRotation || previous.moving != moving || previous.reason != reason || previous.front != TrackId(table.frontClosest?.track) || previous.rear != TrackId(table.rearClosest?.track);
                var state = new TurntableState { id = entry.def.id, angle = table.currentYRotation, target = entry.command == null ? table.targetYRotation : entry.goal, moving = moving, available = reason == null && !moving, reason = reason, front = TrackId(table.frontClosest?.track), rear = TrackId(table.rearClosest?.track), frontEnd = table.frontClosest == null ? -1 : table.frontClosest.isFirst ? 0 : 1, rearEnd = table.rearClosest == null ? -1 : table.rearClosest.isFirst ? 0 : 1, points = points, revision = changed ? ++entityRevision : previous.revision, sampledAt = Protocol.Now };
                entry.lastAngle = table.currentYRotation;
                if (full || changed || Protocol.Now - previous.sampledAt >= 1000) { entry.state = state; values.Add(state); }
            }
            return values.ToArray();
        }
        private void TurntableCommand(Command c)
        {
            string error = null;
            double goal = 0;
            if (!tables.TryGetValue(c.target ?? "", out var entry)) error = "NOT_FOUND";
            else if (entry.command != null) error = "TURNTABLE_BUSY";
            else if (entry.state == null || entry.state.revision != c.expectedRevision || Math.Abs(TurntableRules.Difference(entry.table.currentYRotation, entry.state.angle)) > .001) error = "STALE_REVISION";
            else if (c.index < 0 || c.index >= entry.def.ends.Length) error = "INVALID_VALUE";
            else if (Connected(entry,c)) { entry.command=c; FinishTable(entry,null); return; }
            else if (!TurntableRules.TryCommandGoal(entry.def,c,entry.table.currentYRotation,out goal)) error = "TURNTABLE_MANEUVER_REQUIRED";
            else error = TableSafety(entry, true);
            if (error != null) { Result(new CommandResult { id = c.id, target = c.target, status = "rejected", code = error }); return; }
            entry.goal = (float)goal;
            entry.drivenAngle = entry.table.currentYRotation; entry.command = c;
        }
        private bool Connected(TableEntry entry, Command c) {
            var table=entry.table;
            if(table==null || Math.Abs(TurntableRules.Difference(table.currentYRotation,table.targetYRotation))>.001) return false;
            return TurntableRules.RequestAligned(new TurntableState {
                front=TrackId(table.frontClosest?.track),rear=TrackId(table.rearClosest?.track),
                frontEnd=table.frontClosest==null?-1:table.frontClosest.isFirst?0:1,
                rearEnd=table.rearClosest==null?-1:table.rearClosest.isFirst?0:1
            },entry.def,c);
        }
        private void FinishTable(TableEntry entry, string error)
        {
            var command = entry.command; entry.command = null;
            if (command == null) return;
            if (error == null)
            {
                entry.lastAngle = entry.table.currentYRotation;
                Main.Bridge?.Send(new WireFrame { kind = "state", batch = new GameBatch { epoch = epoch, topologyRevision = topologyRevision, turntables = CaptureTables(true) } });
            }
            Result(new CommandResult { id = command.id, target = command.target, status = error == null ? "applied" : "rejected", code = error });
        }
        // Our component stays active when the game's 200m controller optimiser
        // disables the native controller. Never enable distant GameObjects.
        private void FixedUpdate()
        {
            if (!world || building) return;
            foreach (var entry in tables.Values) if (entry.command != null)
            {
                try { DriveTable(entry); }
                catch (Exception e) { Main.Log("TURNTABLE_UPDATE_FAILED", e); FinishTable(entry,"COMMAND_FAILED"); }
            }
        }
        private void PollTableCommands()
        {
            foreach (var entry in tables.Values) if (entry.command != null) {
                string error = Main.Bridge?.Connected != true ? "GAME_DISCONNECTED" : building ? "TOPOLOGY_CHANGED" : Protocol.Now > entry.command.deadline ? "COMMAND_EXPIRED" : null;
                if (error != null) FinishTable(entry,error);
            }
        }
        private void DriveTable(TableEntry entry)
        {
            var table = entry.table;
            string error = !world || building ? "WORLD_NOT_READY" : Main.Bridge?.Connected != true || !multiplayer.Authority || Main.Config.ReadOnly ? "GAME_DISCONNECTED" : Protocol.Now > entry.command.deadline ? "COMMAND_EXPIRED" : TableSafety(entry, true);
            if (error == null && Math.Abs(TurntableRules.Difference(table.currentYRotation, entry.drivenAngle)) > .01) error = "TURNTABLE_CHANGED_EXTERNALLY";
            if (error != null) { FinishTable(entry, error); return; }
            if (!WorldStreamingInit.IsLoaded || DV.PausePhysicsHandler.Instance.PhysicsHandlingInProcess) return;
            var next = Mathf.MoveTowardsAngle(table.currentYRotation, entry.goal, 10f * Time.fixedDeltaTime);
            entry.controller.SetAngle(TurntableRailTrack.AngleRange0To360(next), true);
            entry.drivenAngle = table.currentYRotation;
            if (Math.Abs(TurntableRules.Difference(table.currentYRotation, entry.goal)) < .001)
            {
                FinishTable(entry, Connected(entry,entry.command) ? null : "TURNTABLE_NOT_CONNECTED");
            }
        }
        [HarmonyPatch(typeof(TurntableController), "FixedUpdate")]
        private static class TableUpdate
        {
            private static bool Prefix(TurntableController __instance)
            {
                var runtime = Main.Runtime;
                if (runtime == null) return true;
                foreach (var entry in runtime.tables.Values)
                    if (entry.table == __instance.turntable) {
                        entry.controller = __instance;
                        if (entry.command != null) return false;
                    }
                return true;
            }
        }
        // Start runs after the native controller has resolved its streamed bridge.
        // Unloaded bridges intentionally have no controller: never scan all Unity
        // objects periodically to rediscover that absence.
        [HarmonyPatch(typeof(TurntableController), "Start")]
        private static class TableControllerLoaded
        {
            private static void Postfix(TurntableController __instance) {
                var runtime = Main.Runtime;
                if (runtime == null) return;
                foreach (var entry in runtime.tables.Values)
                    if (entry.table == __instance.turntable) entry.controller = __instance;
            }
        }
        [HarmonyPatch(typeof(TurntableController), "OnDestroy")]
        private static class TableControllerUnloaded
        {
            private static void Prefix(TurntableController __instance) {
                var runtime = Main.Runtime;
                if (runtime == null) return;
                foreach (var entry in runtime.tables.Values)
                    if (ReferenceEquals(entry.controller, __instance)) {
                        if (entry.command != null) runtime.FinishTable(entry, "TURNTABLE_AREA_UNLOADED");
                        entry.controller = null;
                    }
            }
        }
    }
}
