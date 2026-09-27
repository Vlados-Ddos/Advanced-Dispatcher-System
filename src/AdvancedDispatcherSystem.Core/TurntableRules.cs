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
        public static string Safety(double radius, IEnumerable<CarState> cars, double x, double z, string bridge)
        {
            foreach (var car in cars)
            {
                if (car.track1 == bridge || car.track2 == bridge) return "TURNTABLE_OCCUPIED";
                double distance = Math.Sqrt((car.x-x)*(car.x-x)+(car.z-z)*(car.z-z));
                double speed = Math.Abs(car.speed) / 3.6;
                double clearance = radius + Math.Max(1, car.length) / 2 + 8;
                if (distance < clearance) return "TURNTABLE_OCCUPIED";
                if (speed > .2 && distance < clearance + 20 + speed * speed / 2) return "TURNTABLE_APPROACHING";
            }
            return null;
        }
    }
}
