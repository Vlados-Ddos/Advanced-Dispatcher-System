using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AdvancedDispatcherSystem.Core;
using AdvancedDispatcherSystem.Game;
using DV.Logic.Job;
using DV.ThingTypes;
using PassengerJobs;
using PassengerJobs.Generation;
using PassengerJobs.Platforms;
using PassengerJobs.Injectors;
using UnityEngine;
using JobState = AdvancedDispatcherSystem.Core.JobState;

namespace AdvancedDispatcherSystem.Passenger
{
    // Optional assembly, loaded only when UMM reports Passenger Jobs active.
    // No generation, route initialization or platform controls are invoked here.
    public sealed class Adapter : IPassengerAdapter
    {
        private readonly FieldInfo stations;
        private readonly string expressColor, localColor;
        private string expressName, localName, labelLanguage;
        public string Status { get; private set; } = "loading";
        public Adapter()
        {
            if (Convert.ToInt32(typeof(PassJobType).GetField("Express")?.GetRawConstantValue()) != (int)PassJobType.Express ||
                Convert.ToInt32(typeof(PassJobType).GetField("Local")?.GetRawConstantValue()) != (int)PassJobType.Local ||
                Convert.ToInt32(typeof(CityLoadingTask).GetField("TaskType")?.GetRawConstantValue()) != (int)CityLoadingTask.TaskType)
                throw new NotSupportedException("PASSENGER_API_UNSUPPORTED: task/job enum");
            stations = typeof(RouteManager).GetField("_stations", BindingFlags.NonPublic | BindingFlags.Static);
            if (stations == null || !typeof(IDictionary<string, IPassDestination>).IsAssignableFrom(stations.FieldType))
                throw new NotSupportedException("PASSENGER_API_UNSUPPORTED: RouteManager._stations");
            var booklet = typeof(PassJobType).Assembly.GetType("PassengerJobs.BookletUtility", true);
            expressColor = ReadColor(booklet, "ExpressColor");
            localColor = ReadColor(booklet, "LocalColor");
        }
        private static string ReadColor(Type type, string name)
        {
            var field = type.GetField(name, BindingFlags.Public | BindingFlags.Static);
            if (field == null || field.FieldType != typeof(Color)) throw new NotSupportedException("PASSENGER_API_UNSUPPORTED: " + name);
            return "#" + ColorUtility.ToHtmlStringRGB((Color)field.GetValue(null));
        }
        public void EnrichJob(Job job, JobState state)
        {
            if (!PassJobType.IsPJType(job.jobType)) return;
            state.type = job.jobType == PassJobType.Express ? "PassengerExpress" : "PassengerLocal";
            state.typeColor = job.jobType == PassJobType.Express ? expressColor : localColor;
            state.typeSource = "Passenger Jobs";
            state.typeName = job.jobType == PassJobType.Express ? expressName : localName;
            state.typeLanguage = labelLanguage;
            state.integrationStatus = Status;
            foreach (var license in new[] { LicenseInjector.License1, LicenseInjector.License2 }) {
                if (license == null) continue;
                for (int i = 0; i < state.licenses.Length; i++)
                    if (state.licenses[i] == license.v1.ToString()) state.licenses[i] = license.id;
            }
        }
        public void EnrichTask(Task nativeTask, TaskData task, JobLeg leg)
        {
            if (task is RuralTaskData rural)
            {
                leg.station = rural.stationId;
                leg.to = rural.stationId;
                leg.type = rural.isLoading ? "PassengerBoarding" : "PassengerAlighting";
            }
            // CityLoadingTask inherits WarehouseTask.GetTaskData(), which exports
            // TaskType.Warehouse. Its native runtime type must survive capture.
            else if (nativeTask is CityLoadingTask)
            {
                leg.station = task.destinationTrack?.ID?.yardId;
                leg.type = task.warehouseTaskType == WarehouseTaskType.Loading ? "PassengerBoarding" :
                    task.warehouseTaskType == WarehouseTaskType.Unloading ? "PassengerAlighting" : "PassengerStop";
            }
            else return;
            leg.source = "Passenger Jobs";
            leg.passengerStop = true;
        }
        public StationDef[] CaptureLocations(Func<RailTrack, string> trackId)
        {
            expressName = LocalizationKey.JOB_EXPRESS_NAME.L();
            localName = LocalizationKey.JOB_REGIONAL_NAME.L();
            if (!DisplayText.Usable(expressName)) expressName = null;
            if (!DisplayText.Usable(localName)) localName = null;
            labelLanguage = Main.LanguageCode;
            var registry = stations.GetValue(null) as IDictionary<string, IPassDestination>;
            if (registry == null) throw new NotSupportedException("PASSENGER_API_UNSUPPORTED: registry");
            var values = registry.Values.ToArray(); // Executed only on the Unity thread.
            var result = new List<StationDef>();
            foreach (var station in values)
            {
                if (station == null) continue;
                bool city = station is PassStationData;
                if (city && ((PassStationData)station).Controller == null || station is RuralStationData r && r.Controller == null) continue;
                var pos = station.GetLocation() - WorldMover.currentMove;
                var platformRows = station.GetPlatforms().Select(p => new { platform = p, id = p.Track == null ? null : trackId(p.Track.RailTrack()) }).Where(x => x.id != null).GroupBy(x => x.id, StringComparer.Ordinal).Select(g => g.First()).ToArray();
                var tracks = platformRows.Select(x => x.id).Distinct().ToArray();
                if (tracks.Length == 0) continue; // A configured but unbuilt platform isn't a live stop.
                // RuralStationData also represents single platforms INSIDE native yards.
                // Match the same real station lookup used by Passenger Jobs RuralStationBuilder.
                var native = StationController.GetStationByYardID(station.YardID);
                var name = native != null ? DisplayText.Station(native) : LocalizationKeyExtensions.StationName(station.YardID);
                if (!DisplayText.Usable(name)) name = null;
                var nameKey = native != null ? native.stationInfo.LocalizationKey :
                    LocalizationKeyExtensions.STATION_NAME_KEY + station.YardID.ToLowerInvariant();
                var nameEn = DisplayText.Translation(nameKey, "English");
                var nameRu = DisplayText.Translation(nameKey, "Russian");
                var platformCode = !city && native != null ? station.GetPlatforms().Select(p => p.PlatformID).FirstOrDefault() : null;
                var platformLabel = !city && native != null ? station.GetPlatforms().Select(p => p.Track?.ID?.TrackPartOnly).FirstOrDefault() : null;
                var nativeRows = platformRows.Select(x => new StationTrackDef { id = x.id, name = x.platform.Track?.ID?.TrackPartOnly, fullName = x.platform.Track?.ID?.FullDisplayID, group = x.platform.Track?.ID?.SignIDSubYardPart, direction = 0 }).ToArray();
                result.Add(new StationDef { id = city ? station.YardID : "pj:" + station.YardID, parent = station.YardID,
                    name = name, nameEn = nameEn, nameRu = nameRu, code = station.YardID, platform = platformCode, platformLabel = platformLabel, trackGroup = !city && native != null ? station.GetPlatforms().Select(p=>p.Track?.ID?.SignIDSubYardPart).FirstOrDefault() : null, type = city ? "passengerCity" : native != null ? "passengerPlatform" : "passengerRural", source = "Passenger Jobs",
                    color = native != null ? "#" + ColorUtility.ToHtmlStringRGB(native.stationInfo.StationColor) : localColor,
                    passenger = true, x = pos.x, z = pos.z, tracks = tracks, stationTracks = nativeRows });
                if (city) foreach (var platform in station.GetPlatforms()) {
                    if (platform.Track == null) continue;
                    var rail = platform.Track.RailTrack(); var id = trackId(rail); if (id == null) continue;
                    var set = rail.GetKinkedPointSet(); if (set == null || set.points.Length == 0) continue;
                    var point = set.points[set.points.Length / 2].position;
                    result.Add(new StationDef { id = "pj:platform:" + platform.PlatformID, parent = station.YardID,
                        name = name, nameEn = nameEn, nameRu = nameRu, code = station.YardID, platform = platform.PlatformID, platformLabel = platform.Track?.ID?.TrackPartOnly, trackGroup=platform.Track?.ID?.SignIDSubYardPart, type = "passengerPlatform", source = "Passenger Jobs",
                        color = native != null ? "#" + ColorUtility.ToHtmlStringRGB(native.stationInfo.StationColor) : localColor,
                        passenger = true, x = point.x, z = point.z, tracks = new[] { id }, stationTracks = new[] { new StationTrackDef { id = id, name = platform.Track?.ID?.TrackPartOnly, fullName = platform.Track?.ID?.FullDisplayID, group = platform.Track?.ID?.SignIDSubYardPart, direction = 0 } } });
                }
            }
            Status = result.Count > 0 ? "ready" : "loading";
            return result.OrderBy(s => s.id, StringComparer.Ordinal).ToArray();
        }
        public void Dispose() { }
    }
}
