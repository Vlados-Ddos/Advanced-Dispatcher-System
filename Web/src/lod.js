import { preferences } from "./preferences.js";

// Threshold controls only. They never alter native geometry or game state.
// Larger percentages require a closer zoom; 100% preserves the defaults.
export function lodFactor(key) {
  return (preferences[key] ?? 100) / 100;
}
export function aboveLod(renderer, key, threshold) {
  return renderer.scale > threshold * lodFactor(key);
}
