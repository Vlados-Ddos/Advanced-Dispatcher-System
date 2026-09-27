using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading.Tasks;
using AdvancedDispatcherSystem.Core;
using DV.Signs;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;

namespace AdvancedDispatcherSystem.Game
{
    public sealed partial class Dispatcher
    {
        private sealed class RawSign
        {
            public float[] position, forward;
            public Parameter[] parameters;
            [JsonIgnore] public RailTrack track;
            [JsonIgnore] public bool centered;
            [JsonIgnore] public string source;
        }
        private sealed class Parameter { public int type; public string text; }
        private sealed class Catalogue { public int format { get; set; } public Source[] sources { get; set; } public RawSign[] signs { get; set; } }
        private sealed class Source { public string file { get; set; } public string sha256 { get; set; } }
        private sealed class SignTrack { public RailTrack track; public Bounds bounds; }
        private readonly Dictionary<string, SignState> signStates = new Dictionary<string, SignState>();
        private readonly List<SignState> changedSigns = new List<SignState>();
        private readonly Dictionary<string, List<SignTrack>> signBins = new Dictionary<string, List<SignTrack>>();
        private readonly Dictionary<string, RawSign> observedSigns = new Dictionary<string, RawSign>();
        private readonly Queue<RawSign> pendingSigns = new Queue<RawSign>();
        private Task<Catalogue> catalogueTask;
        private Catalogue catalogue;
        private bool doubleTrackSigns, catalogueQueued, signErrorLogged, signHookFailed;
        private string signsStatus = "loading";
        private static readonly FieldInfo SignParametersField = AccessTools.Field(typeof(Sign), "signParameters");
        private void HookSigns()
        {
            var dt = AccessTools.TypeByName("DV.Signs.CustomSignPlacer");
            doubleTrackSigns = UnityModManagerNet.UnityModManager.FindMod("DoubleTrack")?.Active == true;
            void Install(Type type, string code)
            {
                try {
                    var method = type == null ? null : AccessTools.Method(type, "PlaceSign");
                    if (method == null) throw new MissingMethodException(type?.FullName ?? code, "PlaceSign");
                    harmony.Patch(method, postfix: new HarmonyMethod(typeof(Dispatcher), nameof(PlacedSign)));
                }
                catch (Exception e) { signHookFailed = true; signsStatus = "partial"; Main.Log(code, e); }
            }
            if (dt != null || doubleTrackSigns) Install(dt, "DOUBLETRACK_SIGN_HOOK_FAILED");
            Install(typeof(SignPlacer), "NATIVE_SIGN_HOOK_FAILED");
            // Unity paths captured here; worker below uses only files and copied DTOs.
            string file = Path.Combine(Main.Entry.Path, "Assets", "signs.json"), data = Application.dataPath;
            catalogueTask = Task.Run(() =>
            {
                if (!File.Exists(file)) return null;
                var value = JsonConvert.DeserializeObject<Catalogue>(File.ReadAllText(file));
                if (value?.format != 1 || value.sources == null || value.signs == null) return null;
                foreach (var source in value.sources)
                {
                    if (source.file != Path.GetFileName(source.file)) return null;
                    var path = Path.Combine(data, source.file); if (!File.Exists(path)) return null;
                    using (var stream = File.OpenRead(path)) using (var sha = SHA256.Create())
                        if (!string.Equals(BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", ""), source.sha256, StringComparison.OrdinalIgnoreCase)) return null;
                }
                return value;
            });
        }
        private void ResetSigns()
        {
            signStates.Clear(); changedSigns.Clear(); signBins.Clear(); pendingSigns.Clear(); observedSigns.Clear();
            catalogueQueued = false; signsStatus = "loading"; signErrorLogged = false;
        }
        private IEnumerator BuildSignIndex()
        {
            signBins.Clear(); signStates.Clear(); changedSigns.Clear(); pendingSigns.Clear(); catalogueQueued = false;
            int cursor = 0;
            foreach (var track in tracks)
            {
                if (track == null) continue;
                var set = track.GetKinkedPointSet(); if (set == null || set.points.Length < 2) continue;
                var bounds = new Bounds((Vector3)set.points[0].position, Vector3.zero);
                foreach (var p in set.points) bounds.Encapsulate((Vector3)p.position);
                bounds.Expand(8); var item = new SignTrack { track = track, bounds = bounds };
                for (int x = Mathf.FloorToInt(bounds.min.x / 500); x <= Mathf.FloorToInt(bounds.max.x / 500); x++)
                    for (int z = Mathf.FloorToInt(bounds.min.z / 500); z <= Mathf.FloorToInt(bounds.max.z / 500); z++)
                    { string key = x + ":" + z; if (!signBins.TryGetValue(key, out var bin)) signBins[key] = bin = new List<SignTrack>(); bin.Add(item); }
                if (++cursor % 8 == 0) yield return null;
            }
            foreach (var sign in observedSigns.Values) if (!doubleTrackSigns || sign.source == "DoubleTrack") pendingSigns.Enqueue(sign);
        }
        private static void PlacedSign(object __instance, object[] __args)
        {
            var runtime = Main.Runtime; if (runtime == null || __args.Length == 0) return;
            try
            {
                bool dt = __instance.GetType().FullName == "DV.Signs.CustomSignPlacer";
                if (dt && !runtime.doubleTrackSigns)
                {
                    runtime.doubleTrackSigns = true; runtime.signStates.Clear(); runtime.pendingSigns.Clear(); runtime.RequestRebuild();
                }
                object data = __args[0]; Type type = data.GetType();
                var track = (RailTrack)AccessTools.Method(type, "GetTrack").Invoke(data, null);
                var p = (Vector3)AccessTools.Method(type, "GetPosition").Invoke(data, null) - WorldMover.currentMove;
                var rotation = (Quaternion)AccessTools.Method(type, "GetRotation").Invoke(data, null);
                var sign = (Sign)AccessTools.Method(type, "GetSign").Invoke(data, null);
                runtime.ObserveSign(p, rotation * Vector3.forward, (IEnumerable<SignParameters>)SignParametersField.GetValue(sign), track, dt ? "DoubleTrack" : "native-live", true);
            }
            catch (Exception e) { runtime.SignError(e); }
        }
        [HarmonyPatch(typeof(SignGenerator), "Start")]
        private static class BakedSign
        {
            private static void Prefix(SignGenerator __instance)
            {
                var r = Main.Runtime;
                if (r == null || r.doubleTrackSigns || __instance == null || __instance.data == null) return;
                try { r.ObserveSign(__instance.transform.position - WorldMover.currentMove, __instance.transform.forward, __instance.data.signParameters, null, "native-live", false); }
                catch (Exception e) { r.SignError(e); }
            }
        }
        private void SignError(Exception e) { signsStatus = "partial"; if (!signErrorLogged) { signErrorLogged = true; Main.Log("SIGN_CAPTURE_FAILED", e); } }
        private void ObserveSign(Vector3 p, Vector3 f, IEnumerable<SignParameters> parameters, RailTrack track, string source, bool centered)
        {
            var values = new List<Parameter>(); foreach (var parameter in parameters) values.Add(new Parameter { type = (int)parameter.type, text = parameter.signText });
            var raw = new RawSign { position = new[] { p.x, p.y, p.z }, forward = new[] { f.x, f.y, f.z }, parameters = values.ToArray(), track = track, source = source, centered = centered };
            string key = Math.Round(p.x, 1).ToString(CultureInfo.InvariantCulture) + ":" + Math.Round(p.z, 1).ToString(CultureInfo.InvariantCulture) + ":" + Math.Round(f.x, 1).ToString(CultureInfo.InvariantCulture) + ":" + Math.Round(f.z, 1).ToString(CultureInfo.InvariantCulture);
            if (observedSigns.Count < 12000 || observedSigns.ContainsKey(key)) observedSigns[key] = raw;
            if (pendingSigns.Count < 16000) pendingSigns.Enqueue(raw);
        }
        private void SamplePendingSign()
        {
            if (!catalogueQueued && catalogueTask != null && catalogueTask.IsCompleted)
            {
                catalogueQueued = true;
                if (catalogueTask.IsFaulted) SignError(catalogueTask.Exception);
                else catalogue = catalogueTask.Result;
                if (!doubleTrackSigns && catalogue != null) foreach (var s in catalogue.signs) { s.source = "native-scene"; pendingSigns.Enqueue(s); }
                signsStatus = signHookFailed || signErrorLogged ? "partial" : doubleTrackSigns ? "observed" : catalogue != null ? "indexed" : "partial";
            }
            // Each binding is one bounded unit; no world scan in the update loop.
            if (pendingSigns.Count == 0 || signBins.Count == 0) return;
            var raw = pendingSigns.Dequeue();
            if (doubleTrackSigns && raw.source != "DoubleTrack") return;
            try { BindSign(raw); } catch (Exception e) { SignError(e); }
        }
        private void BindSign(RawSign raw)
        {
            var p = new Vector3(raw.position[0], raw.position[1], raw.position[2]);
            var forward = new Vector3(raw.forward[0], raw.forward[1], raw.forward[2]);
            var center = raw.centered ? p : p + 2 * Vector3.Cross(Vector3.up, forward).normalized;
            RailTrack best = null; double span = 0; int direction = 0; float error = 3, second = float.PositiveInfinity;
            if (!signBins.TryGetValue(Mathf.FloorToInt(center.x / 500) + ":" + Mathf.FloorToInt(center.z / 500), out var bin)) return;
            foreach (var candidate in bin)
            {
                if (candidate.track == null || raw.track != null && candidate.track != raw.track || !candidate.bounds.Contains(center)) continue;
                if (!ProjectSign(candidate.track, center, out var at, out var tangent, out var distance)) continue;
                float alignment = Vector3.Dot(tangent, forward); if (Math.Abs(alignment) < 0.9) continue;
                if (distance < error) { second = error; error = distance; best = candidate.track; span = at; direction = alignment < 0 ? 1 : -1; }
                else second = Math.Min(second, distance);
            }
            if (best == null || second - error < 0.15f) { signsStatus = "partial"; return; }
            string trackId = TrackId(best); if (trackId == null) return;
            var speeds = new List<int>(); var branches = new List<int>(); var types = new List<string>(); bool upcoming = false, arrows = false;
            for (int i = 0; i < raw.parameters.Length; i++)
            {
                var param = raw.parameters[i]; var type = (SignType)param.type; types.Add(type.ToString());
                if (type == SignType.UpcomingJunction || type == SignType.UpcomingJunctionOld) upcoming = true;
                if (param.type < 0 || param.type > 3 || !int.TryParse(param.text, out int speed) || speed <= 0 || speed > 99) continue;
                int branch = -1;
                if (i + 1 < raw.parameters.Length) { if (raw.parameters[i + 1].type == (int)SignType.ArrowLeft) branch = 0; if (raw.parameters[i + 1].type == (int)SignType.ArrowRight) branch = 1; }
                speeds.Add(speed * 10); branches.Add(branch); arrows |= branch >= 0;
            }
            string id = "sign:" + trackId + ":" + direction + ":" + Math.Round(span).ToString(CultureInfo.InvariantCulture);
            var junction = direction > 0 ? best.outJunction : best.inJunction;
            string jid = null; if (junction != null) junctionIds.TryGetValue(junction, out jid);
            var value = new SignState { id = id, track = trackId, junction = jid, x = p.x, z = p.z, span = span, direction = direction, speeds = speeds.ToArray(), branches = branches.ToArray(), types = types.ToArray(), advance = upcoming && arrows, source = raw.source };
            // Prefer a currently observed modification over the authored scene cache.
            if (signStates.TryGetValue(id, out var old) && old.source != "native-scene" && raw.source == "native-scene") return;
            signStates[id] = value; changedSigns.Add(value);
        }
        private static bool ProjectSign(RailTrack track, Vector3 position, out double span, out Vector3 tangent, out float distance)
        {
            span = 0; tangent = Vector3.forward; distance = float.PositiveInfinity;
            var points = track.GetKinkedPointSet()?.points; if (points == null || points.Length < 2) return false;
            for (int i = 0; i + 1 < points.Length; i++)
            {
                Vector3 a = (Vector3)points[i].position, delta = (Vector3)points[i + 1].position - a;
                float t = Mathf.Clamp01(Vector3.Dot(position - a, delta) / Mathf.Max(delta.sqrMagnitude, 0.00001f));
                float square = (position - a - delta * t).sqrMagnitude;
                if (square >= distance) continue;
                distance = square; span = points[i].span + t * points[i].spanToNextPoint; tangent = delta.normalized;
            }
            distance = Mathf.Sqrt(distance); return !float.IsInfinity(distance);
        }
        private StationDef[] CaptureStations()
        {
            var result = new List<StationDef>();
            if (StationController.allStations == null) return result.ToArray();
            foreach (var station in StationController.allStations)
            {
                if (station == null || station.stationInfo == null) continue;
                var ids = new List<string>(); if (station.AllStationTracks != null) foreach (var t in station.AllStationTracks) { var id = TrackId(t); if (id != null) ids.Add(id); }
                var p = station.transform.position - WorldMover.currentMove;
                var info = station.stationInfo;
                // The shipped StationInfo.Type is blank. Authored station names explicitly
                // identify City West/South and the four combined industry & Town locations.
                bool city = info.Name.StartsWith("City ", StringComparison.OrdinalIgnoreCase) || info.Name.EndsWith(" & Town", StringComparison.OrdinalIgnoreCase);
                string name = DisplayText.Station(station);
                result.Add(new StationDef { id = info.YardID, name = name, searchNames = DisplayText.SearchNames(info.LocalizationKey, info.Name), type = info.Type, color = "#" + ColorUtility.ToHtmlStringRGB(info.StationColor), city = city, source = "Derail Valley", x = p.x, z = p.z, tracks = ids.ToArray(), industry = station.warehouseMachineControllers?.Count > 0 });
            }
            return result.ToArray();
        }
    }
}
