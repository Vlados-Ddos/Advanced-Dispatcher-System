import { mapPalette, offsetPolyline } from "./map-palette.js";
import { preferences } from "./preferences.js";
// Planned geometry is separate from live turntable alignment. Joining the two
// specified mouths draws the intended through path without inventing game links.
export function routeLines(route, store) {
  const lines = [];
  for (const id of route?.tracks || []) {
    const points = store.tracks.get(id)?.points;
    if (points?.length >= 4) lines.push(points);
  }
  for (const step of route?.turntables || []) {
    const from = store.tracks.get(step.from)?.points,
      to = store.tracks.get(step.to)?.points;
    if (
      !from ||
      !to ||
      ![0, 1].includes(step.fromEnd) ||
      ![0, 1].includes(step.toEnd)
    )
      continue;
    const a = step.fromEnd === 0 ? 0 : from.length - 2,
      b = step.toEnd === 0 ? 0 : to.length - 2;
    lines.push([from[a], from[a + 1], to[b], to[b + 1]]);
  }
  return lines;
}
export function routeBounds(route, store) {
  const bounds = [Infinity, Infinity, -Infinity, -Infinity];
  for (const line of routeLines(route, store))
    for (let i = 0; i < line.length; i += 2) {
      bounds[0] = Math.min(bounds[0], line[i]);
      bounds[1] = Math.min(bounds[1], line[i + 1]);
      bounds[2] = Math.max(bounds[2], line[i]);
      bounds[3] = Math.max(bounds[3], line[i + 1]);
    }
  return bounds.every(Number.isFinite) ? bounds : null;
}
export function routeHit(route, store, project, x, y) {
  for (const line of routeLines(route, store))
    for (let i = 2; i < line.length; i += 2) {
      const a = project(line[i - 2], line[i - 1]),
        b = project(line[i], line[i + 1]);
      const dx = b[0] - a[0],
        dy = b[1] - a[1];
      const u = Math.max(
        0,
        Math.min(
          1,
          ((x - a[0]) * dx + (y - a[1]) * dy) / (dx * dx + dy * dy || 1),
        ),
      );
      if (Math.hypot(x - a[0] - u * dx, y - a[1] - u * dy) <= 6) return true;
    }
  return false;
}
export function drawRoute(renderer, ctx, route, strong = false) {
  ctx.save();
  ctx.lineJoin = ctx.lineCap = "round";
  ctx.setLineDash([]);
  if (!strong) {
    ctx.strokeStyle = mapPalette.selected;
    ctx.lineWidth = 1.5;
    ctx.setLineDash([3, 3]);
    for (const line of routeLines(route, renderer.store)) {
      const points = [];
      for (let i = 0; i < line.length; i += 2)
        points.push(renderer.project(line[i], line[i + 1]));
      for (const side of [-1, 1]) {
        ctx.beginPath();
        offsetPolyline(
          points,
          (side * 5 * preferences.trackScale) / 100,
        ).forEach((p, i) => (i ? ctx.lineTo(...p) : ctx.moveTo(...p)));
        ctx.stroke();
      }
    }
    ctx.restore();
    return;
  }
  ctx.beginPath();
  for (const line of routeLines(route, renderer.store)) {
    for (let i = 0; i < line.length; i += 2) {
      const p = renderer.project(line[i], line[i + 1]);
      if (i) ctx.lineTo(...p);
      else ctx.moveTo(...p);
    }
  }
  ctx.strokeStyle = renderer.colors.map;
  ctx.lineWidth = ((strong ? 8 : 6) * preferences.trackScale) / 100;
  ctx.stroke();
  ctx.strokeStyle = renderer.colors.accent;
  ctx.lineWidth = ((strong ? 4 : 2.5) * preferences.trackScale) / 100;
  ctx.stroke();
  ctx.restore();
}
