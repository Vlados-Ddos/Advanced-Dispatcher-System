import { lodFactor } from "./lod.js";

export const smooth = (a, b, n) => {
  const u = Math.max(0, Math.min(1, (n - a) / (b - a)));
  return u * u * (3 - 2 * u);
};
// Full-size heads need a rail-scale view: a 4 m vehicle corridor is 16 px.
// Previously the full footprint was reserved at 1 px/m, forcing heads outside
// whole yards and locking those distant cells even when zoomed in.
export const signalDetailScale = 6;
export function signalLod(renderer, signal = null) {
  const geometryApproach = 20 * renderer.scale;
  const factor = signal
    ? lodFactor(
        signal.shuntingSignal === true && signal.classificationKnown === true
          ? "lodShunting"
          : "lodSignals",
      )
    : Math.min(
        renderer.layers?.signals === false ? Infinity : lodFactor("lodSignals"),
        renderer.layers?.shuntingSignals === false
          ? Infinity
          : lodFactor("lodShunting"),
      );
  const approach = geometryApproach / factor;
  if (renderer.signalPreview)
    return {
      visible: true,
      opacity: 1,
      size: 1,
      detail: 1,
      connectors: 1,
    };
  return {
    visible: approach > 3,
    opacity: smooth(3, 7, approach),
    size: Math.min(1, geometryApproach / (20 * signalDetailScale)),
    detail: smooth(20, 20 * signalDetailScale, approach),
    connectors: smooth(20, 30, approach),
  };
}
export function indicatorLod(renderer) {
  if (renderer.signalPreview) return 1;
  const approach = (20 * renderer.scale) / lodFactor("lodIndicators");
  return smooth(20, 20 * signalDetailScale, approach);
}
