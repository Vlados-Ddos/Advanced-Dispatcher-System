import { readSetting, saveSetting, removeSetting } from "./storage.js";
export const interfaceMetrics = Object.freeze({ baseFont: 16, rowHeight: 72 });
export const mapMetrics = Object.freeze({
  label: 13,
  stationLabel: 14,
  signLabel: 12,
  signRadius: 9,
  signalRadius: 5.5,
  minTrainWidth: 4,
});
export const lodSettings = Object.freeze({
  lodSignals: [25, 400],
  lodShunting: [25, 400],
  lodIndicators: [25, 400],
  lodSwitches: [25, 400],
  lodRollingStock: [25, 400],
  lodSigns: [25, 400],
  lodTrackLabels: [25, 400],
  lodVehicleLabels: [25, 400],
  lodBlockLabels: [25, 400],
  lodSpeedLabels: [25, 400],
  lodTechnical: [25, 400],
});
const limits = {
  ...lodSettings,
  interfaceScale: [80, 200],
  textScale: [80, 160],
  labelScale: [60, 200],
  trainLabelScale: [60, 200],
  signalScale: [60, 250],
  trackScale: [50, 250],
  occupancyScale: [50, 250],
  trainScale: [60, 200],
  switchScale: [60, 200],
  branchScale: [60, 200],
  indicatorScale: [60, 250],
  signScale: [60, 200],
  blockLabelScale: [60, 200],
  renderRate: [15, 60],
};
const defaults = Object.fromEntries(
  Object.keys(limits).map((k) => [k, k === "renderRate" ? 60 : 100]),
);
defaults.branchColor = "#ffffff";
defaults.signalTooltip = true;
defaults.switchClick = true;
export function normalizePreferences(value = {}) {
  value = value && typeof value === "object" ? value : {};
  value = { ...value };
  // Preserve the pre-0.7.2 appearance once; subsequent edits are independent.
  for (const [key, old] of [
    ["branchScale", "switchScale"],
    ["indicatorScale", "signalScale"],
    ["signScale", "labelScale"],
  ])
    if (!(key in value) && old in value) value[key] = value[old];
  return {
    signalTooltip:
      typeof value.signalTooltip === "boolean"
        ? value.signalTooltip
        : defaults.signalTooltip,
    switchClick:
      typeof value.switchClick === "boolean"
        ? value.switchClick
        : defaults.switchClick,
    branchColor: /^#[a-f\d]{6}$/i.test(value.branchColor || "")
      ? value.branchColor.toLowerCase()
      : defaults.branchColor,
    ...Object.fromEntries(
      Object.entries(limits).map(([k, [min, max]]) => [
        k,
        (typeof value[k] === "number" ||
          (typeof value[k] === "string" && value[k].trim() !== "")) &&
        Number.isFinite(Number(value[k]))
          ? Math.min(max, Math.max(min, Number(value[k])))
          : defaults[k],
      ]),
    ),
  };
}
export function readPreferences() {
  // Remove the retired preference without touching layers or display settings.
  removeSetting("ads.mapMode");
  try {
    const saved = JSON.parse(readSetting("ads.display") || "{}");
    const normalized = normalizePreferences(saved);
    if (
      saved &&
      typeof saved === "object" &&
      ("signalStyle" in saved ||
        "nativeSignalScale" in saved ||
        !("switchClick" in saved) ||
        ![
          "branchScale",
          "indicatorScale",
          "signScale",
          "branchColor",
          "occupancyScale",
        ].every((k) => k in saved))
    )
      saveSetting("ads.display", JSON.stringify(normalized));
    return normalized;
  } catch (error) {
    removeSetting("ads.display");
    console.warn("ADS_DISPLAY_SETTINGS_RESET", error.name);
    return { ...defaults };
  }
}
export const preferences = readPreferences();
export function applyPreferences() {
  document.documentElement.style.setProperty(
    "--branch-color",
    preferences.branchColor,
  );
  document.documentElement.style.setProperty(
    "--base-font-size",
    interfaceMetrics.baseFont + "px",
  );
  document.documentElement.style.setProperty(
    "--ui-scale",
    String(preferences.interfaceScale / 100),
  );
  document.documentElement.style.setProperty(
    "--text-scale",
    String(preferences.textScale / 100),
  );
  document.documentElement.style.setProperty(
    "--row-height",
    `${rowHeight()}px`,
  );
}
export function setPreference(key, value) {
  if (!limits[key] && key !== "branchColor" && key !== "signalTooltip" && key !== "switchClick") return;
  Object.assign(
    preferences,
    normalizePreferences({ ...preferences, [key]: value }),
  );
  saveSetting("ads.display", JSON.stringify(preferences));
  applyPreferences();
  window.dispatchEvent(new CustomEvent("ads-display", { detail: { key } }));
}
export function rowHeight() {
  return (
    ((interfaceMetrics.rowHeight * preferences.interfaceScale) / 100) *
    Math.max(1, preferences.textScale / 100)
  );
}
export { limits };
