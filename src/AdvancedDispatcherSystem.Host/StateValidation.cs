using AdvancedDispatcherSystem.Core;
using System;

namespace AdvancedDispatcherSystem.Host;

public sealed partial class StateHub
{
    // Reject malformed input before changing dictionaries or publishing a partial revision.
    private void ValidateBatch(GameBatch b)
    {
        static void Require(bool ok) { if (!ok) throw new ArgumentException("INVALID_BATCH"); }
        static bool Id(string id) => !string.IsNullOrEmpty(id) && id.Length <= 160;
        static bool Finite(params double[] values) => values.All(TrackGraph.Finite);
        if(b.routeStates!=null)foreach(var x in b.routeStates)Require(x!=null&&Id(x.id)&&!string.IsNullOrEmpty(x.lifecycle)&&!string.IsNullOrEmpty(x.reservation)&&!string.IsNullOrEmpty(x.mode)&&x.signals!=null&&x.signals.All(Id));
        if(b.routeStates!=null)foreach(var x in b.routeStates)
            if(x.plan?.tracks?.Length>0)Require(x.plan.id==x.id&&ValidRoutePath(x.plan));
        if (b.locations != null) foreach (var x in b.locations) Require(x != null && Id(x.id) && x.tracks != null && x.tracks.All(Id) && x.stationTracks != null && x.stationTracks.All(t => t != null && Id(t.id) && Finite(t.direction)) && Finite(x.x, x.z));
        if (b.signals != null) foreach (var x in b.signals) Require(x != null && x.routeBranches != null && x.routeBranches.Length <= 64 && x.routeBranches.All(Id) && (x.routeIncoming == null || Id(x.routeIncoming)) && x.parts != null && x.parts.Length <= 64 && x.parts.All(p => p != null && Id(p.id) && Finite(p.x,p.y,p.worldX,p.worldZ)) && x.lampLayout != null && x.lampLayout.Length <= 128 && x.lampLayout.All(l => l != null && Id(l.id) && Finite(l.x, l.y,l.brightness) && l.brightness >= 0 && l.brightness <= 1));
        if (b.capabilities != null) {
            Require(Finite(b.capabilities.gameTime,b.capabilities.clockRate,b.capabilities.captureMs,b.capabilities.gameTimeOfDay));
            Require(!b.capabilities.gameTimeOfDayKnown || b.capabilities.gameTimeOfDay >= 0 && b.capabilities.gameTimeOfDay < 24);
            // Weather is optional at runtime, but when the native adapter
            // publishes it the wire contract must stay finite. Reject a
            // malformed reflection value before it reaches JSON persistence
            // or the browser rather than serializing NaN/Infinity.
            var w = b.capabilities.weather;
            if (w != null) Require(Finite(w.rain,w.wetness,w.thunder,w.fog,w.cloudiness));
            if(w?.forecast!=null)foreach(var entry in w.forecast)
                Require(entry!=null&&Finite(entry.durationSeconds,entry.startsInSeconds)&&entry.durationSeconds>=0&&entry.startsInSeconds>=0&&
                    (!entry.timeKnown||entry.hourStart>=0&&entry.hourStart<24&&entry.hourEnd>=0&&entry.hourEnd<=24));
            var settings = b.capabilities.hostSettings;
            if (settings != null)
                Require(Finite(settings.captureBudgetMs) && settings.captureBudgetMs >= 0.3 && settings.captureBudgetMs <= 2);
        }
        if (b.switches != null) foreach (var x in b.switches) Require(Id(x.id));
        if (b.turntables != null) foreach (var x in b.turntables) Require(x != null && Id(x.id) && x.points != null && x.points.Length == 4 && Finite(x.points) && Finite(x.angle, x.target) && (x.front == null || Id(x.front) && x.frontEnd >= 0 && x.frontEnd <= 1) && (x.rear == null || Id(x.rear) && x.rearEnd >= 0 && x.rearEnd <= 1));
        if (b.turntables != null) foreach (var x in b.turntables) {
            Require(Graph.Turntables.TryGetValue(x.id, out var definition));
            Require((x.front == null || definition.ends.Any(e => e.track == x.front && e.end == x.frontEnd)) && (x.rear == null || definition.ends.Any(e => e.track == x.rear && e.end == x.rearEnd)));
        }
        if (b.cars != null) foreach (var x in b.cars)
            Require(Id(x.id) && Id(x.consist) &&
                Finite(x.x, x.z, x.yaw, x.length, x.width, x.span1, x.span2, x.speed, x.cargoAmount, x.throttle, x.trainBrake, x.independentBrake, x.reverser, x.brakePipe, x.mass, x.consistMass, x.availableTraction) &&
                x.mass >= 0 && x.consistMass >= 0 && x.availableTraction >= 0);
        if (b.cars != null) foreach (var x in b.cars) if (x.consistCargo != null) {
            var c = x.consistCargo;
            Require(c.cars >= 0 && c.types != null && c.types.All(v => !string.IsNullOrEmpty(v)) &&
                Finite(c.length) && c.length >= 0 && (!c.lengthKnown || c.length > 0) &&
                c.members != null && c.members.All(Id) && (!c.membershipComplete || c.members.Length == c.cars && c.members.Distinct().Count() == c.cars));
        }
        if (b.players != null) foreach (var x in b.players) Require(Id(x.id) && Finite(x.x, x.z, x.yaw) && (!x.carPoseKnown || Id(x.car) && Finite(x.carX, x.carZ, x.carYaw)));
        if (b.signals != null) foreach (var x in b.signals) Require(x != null && Id(x.id) && (x.authorityTrack==null||Id(x.authorityTrack)) && x.lamps != null && x.aspects != null && x.blinkingLamps != null && Finite(x.x, x.z, x.yaw, x.span, x.passingSpeed, x.reservationSeconds));
        if(b.signals!=null)foreach(var x in b.signals)Require(x.routeIncomingDirection>=-1&&x.routeIncomingDirection<=1&&
            x.routeBranchDirections!=null&&(x.routeBranchDirections.Length==0||x.routeBranchDirections.Length==x.routeBranches.Length)&&x.routeBranchDirections.All(d=>d==1||d==-1));
        if (b.blocks != null) foreach (var x in b.blocks) Require(x != null && Id(x.id) && x.tracks != null && x.extraTracks != null && x.directions != null && x.trains != null && x.tracks.All(Id) && x.extraTracks.All(Id) && Finite(x.length));
        if (b.jobs != null) foreach (var x in b.jobs) Require(x != null && Id(x.id) && x.legs != null && x.cars != null && x.cargo != null && x.licenses != null && x.cars.All(Id) && x.legs.All(l => l != null && l.cars != null && l.cars.All(Id) && l.cargo != null && Finite(l.cargoAmount)) && Finite(x.mass, x.length, x.payment, x.bonus, x.elapsedSeconds, x.bonusLimitSeconds, x.sampledGameTime) && x.mass >= 0);
        if (b.signs != null) foreach (var x in b.signs) Require(x != null && Id(x.id) && Id(x.track) && x.speeds != null && x.branches != null && x.types != null && Finite(x.x, x.z, x.span));
        // `carsKnown` describes the quality of the native owner sample, not
        // occupancy itself. An empty, known owner list is authoritative for
        // a free track. If a track is occupied, a known sample must contain
        // at least one owner so an anonymous obstacle is never accepted.
        if (b.occupancy != null) foreach (var x in b.occupancy) Require(Id(x.id) && (x.cars == null || x.cars.All(Id)) && (!x.carsKnown || !x.occupied || x.cars != null && x.cars.Length > 0));
        foreach (var removed in new[] { b.removedCars, b.removedSignals, b.removedBlocks }) if (removed != null) Require(removed.All(Id));
        if (b.events != null) Require(b.events.All(e => e != null));
    }
}
