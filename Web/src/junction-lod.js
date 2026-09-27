import { smooth } from "./signal-lod.js";
import { lodFactor } from "./lod.js";

// The same continuous screen-space LOD rule as signals. No timers, state
// changes, or per-junction caches: geometry, picking and outlines share it.
export function junctionLod(renderer) {
  const geometryScale = renderer.scale;
  const scale = geometryScale / lodFactor("lodSwitches");
  return {
    opacity: smooth(0.015, 0.08, scale),
    size: Math.max(0.2, Math.min(1, geometryScale / 0.8)),
    arrow: smooth(0.05, 0.22, scale),
    detail: smooth(0.25, 0.8, scale),
  };
}
