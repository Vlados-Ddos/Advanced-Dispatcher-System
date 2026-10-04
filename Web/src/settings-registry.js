// Only personal browser configuration belongs here. Accounts, cookies, game
// state and unknown keys must never be removed by the settings reset action.
// tests/browser-settings-reset.mjs checks this inventory against every source.
export const browserSettingKeys = Object.freeze([
  "ads.display", // Scale, LOD, visual sizes/colours, tooltip and switch click.
  "ads.layers",
  "ads.layerGroups",
  "ads.panels",
  "ads.theme",
  "ads.colors",
  "ads.camera",
  "ads.locationGroups",
  "ads.hostLanguage", // Cached host language; the current Host supplies it again.
  "ads.language", // Retired personal language preference.
  "ads.mapMode", // Retired view mode.
]);
