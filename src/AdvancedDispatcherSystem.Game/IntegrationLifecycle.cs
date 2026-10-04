using System;
using System.Linq;
using AdvancedDispatcherSystem.Core;
using UnityEngine;
using UnityModManagerNet;

namespace AdvancedDispatcherSystem.Game
{
    public sealed partial class Dispatcher
    {
        private float nextIntegrations, retrySignals, retryMultiplayer, retryPassenger;
        private IPassengerAdapter passenger;
        private string passengerError, passengerVersion = "";
        private StationDef[] lastLocations = new StationDef[0];
        private bool shuttingDown;
        private bool multiplayerLoaded;
        private void RefreshIntegrations()
        {
            if (shuttingDown) return;
            if (Time.realtimeSinceStartup < nextIntegrations) return;
            nextIntegrations = Time.realtimeSinceStartup + 2;
            bool signalsEnabled = UnityModManager.FindMod("DVSignals")?.Active == true;
            if (!signalsEnabled && signals != null) DropSignals(null);
            if (!signalsEnabled) signalsError = null;
            else if (signals == null && Time.realtimeSinceStartup >= retrySignals)
            {
                try
                {
                    signals = (ISignalsAdapter)Main.LoadAdapter("AdvancedDispatcherSystem.Signals.dll", "AdvancedDispatcherSystem.Signals.Adapter");
                    signalsError = null;
                }
                catch (Exception e) { DropSignals(e); }
            }
            var pj = UnityModManager.FindMod("PassengerJobs");
            passengerVersion = pj?.Info.Version ?? "";
            bool passengerEnabled = pj?.Active == true;
            if (!passengerEnabled && passenger != null) DropPassenger(null);
            if (!passengerEnabled) passengerError = null;
            else if (passenger == null && Time.realtimeSinceStartup >= retryPassenger)
            {
                try
                {
                    passenger = (IPassengerAdapter)Main.LoadAdapter("AdvancedDispatcherSystem.Passenger.dll", "AdvancedDispatcherSystem.Passenger.Adapter");
                    passengerError = null; RequestJobs();
                }
                catch (Exception e) { DropPassenger(e); }
            }
            bool mpEnabled = UnityModManager.FindMod("Multiplayer")?.Active == true;
            if (!mpEnabled && multiplayerLoaded)
            {
                DisposeIntegration(multiplayer, "MULTIPLAYER_DISPOSE_FAILED");
                multiplayer = new StandaloneAdapter(); multiplayerLoaded = false;
            }
            else if (mpEnabled && (!multiplayerLoaded || multiplayer is UnavailableMultiplayer) && Time.realtimeSinceStartup >= retryMultiplayer)
            {
                multiplayerLoaded = true;
                try
                {
                    var next = (IMultiplayerAdapter)Main.LoadAdapter("AdvancedDispatcherSystem.Multiplayer.dll", "AdvancedDispatcherSystem.Multiplayer.Adapter");
                    DisposeIntegration(multiplayer, "MULTIPLAYER_DISPOSE_FAILED"); multiplayer = new GuardedMultiplayerAdapter(next, MultiplayerFailed);
                }
                catch (Exception e) { MultiplayerFailed(e); }
            }
        }
        private void DropPassenger(Exception error)
        {
            DisposeIntegration(passenger, "PASSENGER_DISPOSE_FAILED"); passenger = null;
            passengerError = error == null ? null : "unavailable";
            retryPassenger = error == null ? 0 : Time.realtimeSinceStartup + 30;
            RequestJobs();
            if (error != null) Main.Log("PASSENGER_ADAPTER_FAILED", error);
        }
        private StationDef[] CaptureLocations()
        {
            // Names are refreshed with the host language without rebuilding track topology
            // (a topology rebuild would interrupt unrelated dispatcher routes).
            var locations = new System.Collections.Generic.Dictionary<string, StationDef>(StringComparer.Ordinal);
            foreach (var station in CaptureStations()) locations[station.id] = station;
            foreach (var station in PassengerLocations()) {
                if (locations.TryGetValue(station.id, out var native)) {
                    native.passenger = station.passenger;
                    native.nameEn = station.nameEn; native.nameRu = station.nameRu; native.code = station.code;
                    var tracks = new System.Collections.Generic.HashSet<string>(native.tracks);
                    foreach (var id in station.tracks) tracks.Add(id);
                    native.tracks = new System.Collections.Generic.List<string>(tracks).ToArray();
                    var metadata = new System.Collections.Generic.Dictionary<string, StationTrackDef>(StringComparer.Ordinal);
                    foreach (var row in native.stationTracks ?? new StationTrackDef[0]) if (row?.id != null) metadata[row.id] = row;
                    foreach (var row in station.stationTracks ?? new StationTrackDef[0]) if (row?.id != null) metadata[row.id] = row;
                    native.stationTracks = new System.Collections.Generic.List<StationTrackDef>(metadata.Values).OrderBy(row => row.id, StringComparer.Ordinal).ToArray();
                } else locations[station.id] = station;
            }
            return new System.Collections.Generic.List<StationDef>(locations.Values).ToArray();
        }
        private StationDef[] PassengerLocations()
        {
            try { return passenger?.CaptureLocations(TrackId) ?? new StationDef[0]; }
            catch (Exception e) { DropPassenger(e); return new StationDef[0]; }
        }
        private void DropSignals(Exception error)
        {
            var previous = signals; signals = null;
            DisposeIntegration(previous, "SIGNALS_DISPOSE_FAILED");
            foreach (var id in signalStates.Keys) removedSignals.Add(id);
            foreach (var id in blockStates.Keys) removedBlocks.Add(id);
            signalStates.Clear(); blockStates.Clear(); changedSignals.Clear(); changedBlocks.Clear();
            if (error == null) { signalsError = null; retrySignals = 0; return; }
            signalsError = "unavailable"; retrySignals = Time.realtimeSinceStartup + 30;
            Main.Log("SIGNALS_CAPTURE_FAILED", error);
        }
        private void MultiplayerFailed(Exception error)
        {
            DisposeIntegration(multiplayer, "MULTIPLAYER_DISPOSE_FAILED");
            multiplayer = new UnavailableMultiplayer(); retryMultiplayer = Time.realtimeSinceStartup + 30;
            Main.Log("MULTIPLAYER_ADAPTER_FAILED", error);
        }
        private PlayerState[] CapturePlayers()
        {
            try { return multiplayer.CapturePlayers(); }
            catch (Exception e) { MultiplayerFailed(e); return new PlayerState[0]; }
        }
        private static void DisposeIntegration(IDisposable adapter, string code)
        {
            try { adapter?.Dispose(); }
            catch (Exception e) { Main.Log(code, e); }
        }
    }
}
