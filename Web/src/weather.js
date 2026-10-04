// Presentation helpers for the native WeatherState projection. They never
// infer conditions from time, browser locale, or external weather services.
export function weatherState(weather) {
  if (weather?.dataQuality !== "ready") return "unknown";
  return ["clear", "cloudy", "rain", "heavyRain", "fog", "thunder"].includes(weather.state)
    ? weather.state
    : "unknown";
}

export function weatherLabelKey(weather) {
  const native = nativeIcons[weather?.icon];
  if (weather?.dataQuality === "ready" && native) return native;
  const state = weatherState(weather);
  return "weather" + state[0].toUpperCase() + state.slice(1);
}

const nativeIcons = Object.freeze({
  Thunder: "weatherThunder", Overcast: "weatherOvercast",
  PartlyCloudy_Day: "weatherPartlyCloudyDay", PartlyCloudy_Night: "weatherPartlyCloudyNight",
  Clear_Day: "weatherClear", Clear_Night: "weatherClearNight",
  LightRain_Day: "weatherLightRainDay", LightRain_Night: "weatherLightRainNight", HeavyRain: "weatherHeavyRain",
  LightFog_Day: "weatherLightFogDay", LightFog_Night: "weatherLightFogNight", HeavyFog: "weatherHeavyFog",
});

export function weatherIconKey(weather) {
  return weatherLabelKey(weather);
}

// Native WeatherState exposes the normalized scalar inputs used to derive the
// current icon. Keep these values visible when the game marks them known so the
// panel does not discard authoritative rain/cloud/fog/thunder information.
const scalarFields = [
  ["rainKnown", "rain", "weatherRainAmount"],
  ["cloudinessKnown", "cloudiness", "weatherCloudinessAmount"],
  ["thunderKnown", "thunder", "weatherThunderAmount"],
  ["fogKnown", "fog", "weatherFogAmount"],
];

export function weatherMeasurements(weather) {
  if (weather?.dataQuality !== "ready") return [];
  return scalarFields.flatMap(([known, value, key]) => {
    const amount = Number(weather[value]);
    return weather[known] === true && Number.isFinite(amount)
      ? [{ key, value: amount }]
      : [];
  });
}

// Format the game's current time-of-day, supplied by
// WorldClockController rather than the browser/system clock. Keep this helper
// pure so the UI can be tested with synthetic capability snapshots.
export function formatGameTime(capabilities) {
  if (capabilities?.gameTimeOfDayKnown !== true) return null;
  const hours = Number(capabilities.gameTimeOfDay);
  if (!Number.isFinite(hours)) return null;
  let total = Math.round(hours * 60);
  total = ((total % 1440) + 1440) % 1440;
  return String(Math.floor(total / 60)).padStart(2, "0") + ":" +
    String(total % 60).padStart(2, "0");
}
