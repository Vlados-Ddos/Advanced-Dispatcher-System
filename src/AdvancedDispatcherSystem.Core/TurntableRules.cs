using System;
using System.Collections.Generic;

namespace AdvancedDispatcherSystem.Core
{
    public static class TurntableRules
    {
        public static double Difference(double a, double b) => ((a - b) % 360 + 540) % 360 - 180;
        public static int NativeMouth(TurntableDef table, double angle) {
            for(int i=0;i<table.ends.Length;i++) if(Math.Abs(Difference(table.ends[i].angle,angle)) < .5) return i;
            return -1;
        }
        public static bool TryAlignment(TurntableDef table, int from, int to, double current, out double angle) {
            angle = 0;
            if (table?.ends == null || from < 0 || to < 0 || from >= table.ends.Length || to >= table.ends.Length || from == to) return false;
            // Both native 0.5-degree connection intervals must overlap. Aim at
            // their midpoint, not one mouth's centre (which can miss the other).
            double delta = Difference(table.ends[to].angle - 180, table.ends[from].angle);
            if (Math.Abs(delta) >= .999) return false;
            double candidate = (table.ends[from].angle + delta/2 + 360) % 360;
            if (NativeMouth(table,candidate)!=from || NativeMouth(table,candidate+180)!=to) return false;
            angle=NearestOrientation(current,candidate); return true;
        }
        public static double NearestOrientation(double current, double angle) => Math.Abs(Difference(angle, current)) <= Math.Abs(Difference(angle + 180, current)) ? (angle + 360) % 360 : (angle + 540) % 360;
        public static bool RequestAligned(TurntableState state, TurntableDef def, Command c) {
            if (state == null || state.moving || c.index < 0 || c.index >= def.ends.Length) return false;
            var end=def.ends[c.index];
            if(c.from != null || c.to != null) return c.from==end.track && (c.fromEnd<0 || c.fromEnd==end.end) && c.to!=null && Aligned(state,new TurntableStep {from=c.from,to=c.to,fromEnd=end.end,toEnd=c.toEnd});
            return state.front==end.track && state.frontEnd==end.end || state.rear==end.track && state.rearEnd==end.end;
        }
        public static bool TryCommandGoal(TurntableDef def, Command c, double current, out double goal) {
            goal=0;
            if(c.index<0 || c.index>=def.ends.Length) return false;
            if(c.from != null || c.to != null) {
                if(c.from!=def.ends[c.index].track || c.fromEnd>=0 && c.fromEnd!=def.ends[c.index].end) return false;
                for(int i=0;i<def.ends.Length;i++) if(def.ends[i].track==c.to && (c.toEnd<0 || def.ends[i].end==c.toEnd))
                    if(TryAlignment(def,c.index,i,current,out goal)) return true;
                return false;
            }
            goal=NearestOrientation(current,def.ends[c.index].angle);
            return NativeMouth(def,goal)==c.index || NativeMouth(def,goal+180)==c.index;
        }
        public static bool Aligned(TurntableState state, TurntableStep step) => state != null && !state.moving &&
            ((state.front == step.from && state.rear == step.to && (step.fromEnd<0 || state.frontEnd==step.fromEnd) && (step.toEnd<0 || state.rearEnd==step.toEnd)) || (state.front == step.to && state.rear == step.from && (step.toEnd<0 || state.frontEnd==step.toEnd) && (step.fromEnd<0 || state.rearEnd==step.fromEnd)));
        private static double DistanceToCar(double x, double z, CarState car)
        {
            // Treat the runtime body as an oriented rectangle.  The previous
            // centre-circle test (radius + length/2) marked parallel cars
            // outside the table's clearance as occupants.  Rotation and
            // width come from the native TrainCar sample, so a body that
            // actually overlaps the bridge remains blocked.
            var radians = car.yaw * Math.PI / 180.0;
            var cos = Math.Cos(radians); var sin = Math.Sin(radians);
            var dx = x - car.x; var dz = z - car.z;
            // Unity's forward is +Z at yaw=0 and +X at yaw=90.
            var along = dx * sin + dz * cos;
            var across = dx * cos - dz * sin;
            var halfLength = Math.Max(1, car.length) / 2;
            var halfWidth = Math.Max(1, car.width) / 2;
            var outsideX = Math.Max(Math.Abs(along) - halfLength, 0);
            var outsideZ = Math.Max(Math.Abs(across) - halfWidth, 0);
            return Math.Sqrt(outsideX * outsideX + outsideZ * outsideZ);
        }
        public static string Safety(double radius, IEnumerable<CarState> cars, double x, double z, string bridge)
        {
            foreach (var car in cars)
            {
                // Persistent Jobs removes the runtime TrainCar during suspend
                // and leaves the last sampled geometry in the dispatch cache.
                // That lifecycle state is not physical occupancy and must not
                // make a free table appear occupied until Resume rebinds it.
                if (!string.IsNullOrEmpty(car.availability) &&
                    !string.Equals(car.availability, "available", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (car.track1 == bridge || car.track2 == bridge) return "TURNTABLE_OCCUPIED";
                double distance = DistanceToCar(x, z, car);
                double speed = Math.Abs(car.speed) / 3.6;
                // The native controller searches for track ends within its
                // own radius.  Add the documented 8 m dispatch safety margin
                // to the *rectangle-vs-circle* distance, instead of folding
                // half the body length into a centre-radius heuristic.
                double clearance = radius + 8;
                if (distance < clearance) return "TURNTABLE_OCCUPIED";
                if (speed > .2 && distance < clearance + 20 + speed * speed / 2) return "TURNTABLE_APPROACHING";
            }
            return null;
        }
    }
}
