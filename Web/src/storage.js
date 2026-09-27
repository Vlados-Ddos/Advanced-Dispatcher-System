const failures = new Set();
let reported = false;
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
