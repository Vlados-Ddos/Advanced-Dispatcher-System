using System;
using System.Collections;
using System.Globalization;
using System.Reflection;
using AdvancedDispatcherSystem.Core;
using UnityEngine;

namespace AdvancedDispatcherSystem.Game
{
    // Exact DV.WeatherSystem 1.0.1 adapter. The installed API exposes the
    // authoritative values on WeatherDriver (RainValue, WetnessValue,
    // ThunderValue, IsRaining) and the current visual cloud/fog snapshot on
    // WeatherPresetManager.LerpedSnapshot. Reflection is used only to keep
    // the optional assembly version tolerant; there is no recursive probing,
    // name guessing, or external weather source.
    internal static class WeatherCapture
    {
        private static WeatherState cached = new WeatherState();
        private static float next;
        private static Type driverType;
        private static object driver;
        private static Type forecasterType;
        private static object forecaster;
        private static bool loggedUnavailable;
        private static bool loggedForecastUnavailable;
        private static bool loggedIconUnavailable;

        public static void Reset()
        {
            cached = new WeatherState(); next = 0;
            driver = null; driverType = null; forecaster = null; forecasterType = null;
            loggedUnavailable = loggedForecastUnavailable = loggedIconUnavailable = false;
        }

        public static WeatherState Read()
        {
            if (Time.realtimeSinceStartup < next) return cached;
            next = Time.realtimeSinceStartup + 1f;
            try
            {
                var value = CaptureExact();
                cached = value;
                if (value.dataQuality == "ready") loggedUnavailable = false;
            }
            catch (Exception e)
            {
                cached = new WeatherState { state = "unknown", dataQuality = "unavailable", sampledAt = Protocol.Now };
                // Unity can recreate the weather singleton after a world
                // transition. Drop the cached reflection handles so the next
                // sample binds the new native instance.
                driver = null; driverType = null; forecaster = null; forecasterType = null;
                if (!loggedUnavailable) { loggedUnavailable = true; Main.Log("WEATHER_CAPTURE_UNAVAILABLE", e); }
            }
            return cached;
        }

        private static WeatherState CaptureExact()
        {
            driverType ??= Type.GetType("DV.WeatherSystem.WeatherDriver, DV.WeatherSystem");
            if (driverType == null) return Unavailable();
            if (driver is UnityEngine.Object destroyed && destroyed == null) driver = null;
            driver ??= StaticMember(driverType, "Instance");
            if (driver == null) return Unavailable();

            var rain = OverridableCurrent(driver, "RainValue");
            var wetness = OverridableCurrent(driver, "WetnessValue");
            var thunder = OverridableCurrent(driver, "ThunderValue");
            var fog = InvokeScalar(driver, "GetLocalFogDensity");
            var manager = InstanceMember(driver, "manager");
            var snapshot = manager == null ? null : InstanceMember(manager, "LerpedSnapshot");
            var cloud = snapshot == null ? double.NaN : ReadScalar(snapshot, "cloudCoverage");
            // The one-sample interpretation is the authoritative current icon.
            // It is deliberately kept separate from the future
            // sequence below: WeatherForecastItem averages describe a forecast
            // bucket and must not replace the live WeatherDriver values.
            var currentForecast = NativeCurrentForecast(driver);
            var currentIcon = currentForecast == null ? null : NativeIcon(InstanceMember(currentForecast, "iconType")?.ToString());
            if (currentForecast != null)
            {
                var nativeFog = ReadScalar(currentForecast, "averageFog") / 100d;
                var nativeCloud = ReadScalar(currentForecast, "averageCloudiness") / 100d;
                if (!Valid(fog) && Valid(nativeFog)) fog = nativeFog;
                if (!Valid(cloud) && Valid(nativeCloud)) cloud = nativeCloud;
            }

            bool anyCondition = Valid(rain) || Valid(thunder) || Valid(fog) || Valid(cloud);
            string state;
            // WeatherForecaster.InterpretData (installed DV 1.0.1 IL):
            // rounded thunder*100 > 0.7, rain > 5% / >= 8%, fog > 60%,
            // partly cloudy > 50%. Wetness alone does not imply clear skies.
            if (currentIcon != null) state = currentIcon;
            else if (Valid(thunder) && Math.Round(thunder * 100d) > .7) state = "thunder";
            else if (Valid(rain) && Math.Round(rain * 100d) >= 8) state = "heavyRain";
            else if (Valid(rain) && Math.Round(rain * 100d) > 5) state = "rain";
            else if (Valid(fog) && Math.Round(fog * 100d) > 60) state = "fog";
            else if (Valid(cloud) && Math.Round(cloud * 100d) > 50) state = "cloudy";
            else state = anyCondition ? "clear" : "unknown";

            var result = Build(state, rain, wetness, thunder, fog, cloud);
            result.icon = currentForecast == null ? null : InstanceMember(currentForecast,"iconType")?.ToString();
            var future = NativeForecast(driver);
            result.forecastKnown = future.known;
            result.forecast = future.entries;
            return result;
        }

        private static WeatherState Build(string state, double rain, double wetness, double thunder, double fog, double cloud)
        {
            bool any = Valid(rain) || Valid(wetness) || Valid(thunder) || Valid(fog) || Valid(cloud);
            return new WeatherState {
                state = state,
                dataQuality = any ? "ready" : "unavailable",
                rainKnown = Valid(rain), wetnessKnown = Valid(wetness), thunderKnown = Valid(thunder),
                fogKnown = Valid(fog), cloudinessKnown = Valid(cloud),
                rain = FiniteOrZero(rain), wetness = FiniteOrZero(wetness), thunder = FiniteOrZero(thunder),
                fog = FiniteOrZero(fog), cloudiness = FiniteOrZero(cloud), sampledAt = Protocol.Now
            };
        }

        private sealed class ForecastCapture
        {
            public bool known;
            public WeatherForecastEntry[] entries = Array.Empty<WeatherForecastEntry>();
        }

        private static object NativeCurrentForecast(object driver)
        {
            try
            {
                var type = ForecasterType();
                if (type == null) return null;
                var instance = ForecasterInstance(type);
                var current = InstanceMember(driver, "CurrentChungusState");
                if (instance == null || current == null) return null;
                var method = Array.Find(type.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance), candidate => candidate.Name == "InterpretData" && candidate.GetParameters().Length == 1);
                if (method == null) return null;
                var stateType = current.GetType();
                var list = Activator.CreateInstance(typeof(System.Collections.Generic.List<>).MakeGenericType(stateType));
                ((IList)list).Add(current);
                var result = method.Invoke(instance, new[] { list });
                loggedIconUnavailable = false;
                return result;
            }
            catch (Exception e)
            {
                if (!loggedIconUnavailable) { loggedIconUnavailable = true; Main.Log("WEATHER_ICON_UNAVAILABLE", e); }
                return null;
            }
        }

        private static ForecastCapture NativeForecast(object driver)
        {
            var capture = new ForecastCapture();
            try
            {
                var type = ForecasterType();
                var instance = ForecasterInstance(type);
                if (type == null || instance == null) return capture;

                if (!InvokeBool(instance, "HasValidForecastForToday")) return capture;

                var values = InstanceMember(instance, "interpretedData") as IEnumerable;
                if (values == null) return capture;
                var current = InstanceMember(driver, "CurrentChungusState");
                var now = current == null ? double.NaN : ReadScalar(current, "dateTime");
                if (!Valid(now)) return capture;
                var result = new System.Collections.Generic.List<WeatherForecastEntry>();
                foreach (var value in values)
                {
                    if (value == null) continue;
                    var state = NativeIcon(InstanceMember(value, "iconType")?.ToString());
                    if (state == null) state = "unknown";
                    var start = ReadScalar(value, "firstSampleTimestamp");
                    var dayFraction = ReadScalar(value, "sampledDataDuration");
                    var durationKnown = Valid(dayFraction) && dayFraction > 0;
                    // Native timestamps/duration are fractions of a game day.
                    // Do not present elapsed buckets from today's forecast as
                    // upcoming weather. Include the bucket covering now.
                    if (!Valid(start) || !durationKnown || start + dayFraction <= now) continue;
                    result.Add(new WeatherForecastEntry {
                        state = state,
                        icon = InstanceMember(value, "iconType")?.ToString(),
                        hourStart = Convert.ToInt32(InstanceMember(value,"hourStart"),CultureInfo.InvariantCulture),
                        hourEnd = Convert.ToInt32(InstanceMember(value,"hourEnd"),CultureInfo.InvariantCulture),
                        timeKnown = true,
                        startsInSeconds = Math.Max(0, (start - now) * 86400d),
                        durationKnown = durationKnown,
                        durationSeconds = durationKnown ? dayFraction * 86400d : 0d,
                    });
                }
                if (result.Count == 0) return capture;
                capture.known = true;
                capture.entries = result.ToArray();
                loggedForecastUnavailable = false;
            }
            catch (Exception e)
            {
                // Forecast support is optional across game builds. Current
                // scalar weather remains usable when this DTO is unavailable.
                if (!loggedForecastUnavailable) { loggedForecastUnavailable = true; Main.Log("WEATHER_FORECAST_UNAVAILABLE", e); }
            }
            return capture;
        }

        private static Type ForecasterType()
        {
            return forecasterType ??= Type.GetType("DV.WeatherSystem.WeatherForecaster, DV.WeatherSystem");
        }

        private static object ForecasterInstance(Type type)
        {
            if (type == null) return null;
            if (forecaster is UnityEngine.Object destroyed && destroyed == null) forecaster = null;
            return forecaster ??= StaticMember(type, "Instance");
        }

        private static bool InvokeBool(object instance, string methodName)
        {
            var method = instance.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
            return method != null && Convert.ToBoolean(method.Invoke(instance, null), CultureInfo.InvariantCulture);
        }

        private static string NativeIcon(string icon)
        {
            if (string.IsNullOrEmpty(icon)) return null;
            if (icon == "Thunder") return "thunder";
            if (icon == "HeavyRain") return "heavyRain";
            if (icon.StartsWith("LightRain", StringComparison.Ordinal)) return "rain";
            if (icon == "HeavyFog" || icon.StartsWith("LightFog", StringComparison.Ordinal)) return "fog";
            if (icon == "Overcast" || icon.StartsWith("PartlyCloudy", StringComparison.Ordinal)) return "cloudy";
            if (icon.StartsWith("Clear", StringComparison.Ordinal)) return "clear";
            return null;
        }

        private static WeatherState Unavailable() => new WeatherState { state = "unknown", dataQuality = "unavailable", sampledAt = Protocol.Now };

        private static object StaticMember(Type type, string name)
        {
            var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            if (property != null) return property.GetValue(null, null);
            var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            return field?.GetValue(null);
        }

        private static object InstanceMember(object instance, string name)
        {
            var type = instance.GetType();
            var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (property != null) return property.GetValue(instance, null);
            var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return field?.GetValue(instance);
        }

        private static double OverridableCurrent(object instance, string propertyName)
        {
            var wrapper = InstanceMember(instance, propertyName);
            return wrapper == null ? double.NaN : ReadScalar(wrapper, "CurrentValue");
        }

        private static double InvokeScalar(object instance, string methodName)
        {
            var method = instance.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
            if (method == null) return double.NaN;
            return ToDouble(method.Invoke(instance, null));
        }

        private static double ReadScalar(object instance, string memberName)
        {
            return ToDouble(InstanceMember(instance, memberName));
        }

        private static double ToDouble(object value)
        {
            try { return value == null ? double.NaN : Convert.ToDouble(value, CultureInfo.InvariantCulture); }
            catch { return double.NaN; }
        }

        private static double FiniteOrZero(double value) => Valid(value) ? Math.Max(0, Math.Min(1, value)) : 0;
        private static bool Valid(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
