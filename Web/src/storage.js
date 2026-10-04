import { browserSettingKeys } from "./settings-registry.js";

const failures = new Set();
let reported = false;
let resetting = false;
function access(operation, key, action, fallback) {
  const id = operation + ":" + key;
  try {
    const result = action();
    failures.delete(id);
    return result;
  } catch (error) {
    failures.add(id);
    if (!reported) {
      reported = true;
      console.warn("ADS_SETTINGS_STORAGE_UNAVAILABLE", error.name);
    }
    return fallback;
  }
}
export const readSetting = (key) =>
  access("read", key, () => localStorage.getItem(key), null);
export const saveSetting = (key, value) =>
  resetting && browserSettingKeys.includes(key) ? false :
  access(
    "write",
    key,
    () => {
      localStorage.setItem(key, value);
      return true;
    },
    false,
  );
export const removeSetting = (key) =>
  access(
    "write",
    key,
    () => {
      localStorage.removeItem?.(key);
      return true;
    },
    false,
  );
export const settingsVolatile = () => failures.size > 0;
export function resetStoredBrowserSettings() {
  let success = true;
  for (const key of browserSettingKeys) if (!removeSetting(key)) success = false;
  // pagehide saves the current camera. Prevent old runtime values from
  // restoring cleared preferences while the requested reload is in progress.
  resetting = success;
  return success;
}
export function cancelBrowserSettingsReset() {
  resetting = false;
}
