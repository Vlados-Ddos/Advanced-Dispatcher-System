import { preferences } from "./preferences.js";
export function applyTurntables(store, values) {
  for (const state of values || []) {
    const def = store.tableDefs.get(state.id),
      bridge = store.tracks.get(def?.track);
    if (!bridge) continue;
    bridge.points = state.points;
    bridge.spans = [0, bridge.length];
    bridge.a = state.front ? [{ track: state.front, end: state.frontEnd }] : [];
    bridge.b = state.rear ? [{ track: state.rear, end: state.rearEnd }] : [];
    for (const end of def.ends) {
      const track = store.tracks.get(end.track);
      if (!track) continue;
      const key = end.end === 0 ? "a" : "b";
      track[key] = (track[key] || []).filter(
        (link) => link.track !== def.track,
      );
      if (state.front === end.track && state.frontEnd === end.end)
        track[key].push({ track: def.track, end: 0 });
      if (state.rear === end.track && state.rearEnd === end.end)
        track[key].push({ track: def.track, end: 1 });
    }
  }
}
export function drawTurntables(renderer, ctx) {
  if (!renderer.layers.turntables) return;
  for (const def of renderer.store.tableDefs.values()) {
    const state = renderer.store.turntables.get(def.id);
    const [x, y] = renderer.project(def.x, def.z),
      radius = Math.max(9, def.radius * renderer.scale);
    if (
      x + radius < 0 ||
      y + radius < 0 ||
      x - radius > renderer.width ||
      y - radius > renderer.height
    )
      continue;
    ctx.save();
    ctx.strokeStyle = state?.moving
      ? renderer.colors.yellow
      : renderer.colors.accent;
    ctx.lineWidth = 1.4;
    ctx.beginPath();
    ctx.arc(x, y, radius, 0, Math.PI * 2);
    ctx.stroke();
    if (state?.points?.length === 4) {
      ctx.beginPath();
      ctx.moveTo(...renderer.project(state.points[0], state.points[1]));
      ctx.lineTo(...renderer.project(state.points[2], state.points[3]));
      ctx.lineWidth = (4 * preferences.trackScale) / 100;
      ctx.stroke();
    }
    ctx.restore();
  }
}
