import { groupSignalEntries } from "./signal-groups.js";
import { preferences } from "./preferences.js";
import { junctionBadge } from "./junction-display.js";
import { signalLod } from "./signal-lod.js";
import { signalBearing, signalMovement } from "./signal-display.js";
import { intersection } from "./track-geometry.js";

export function signalLane(renderer, signal) {
  const anchor = signalAnchor(signal, renderer.store);
  if (!anchor) return { gap: Infinity, corridor: 2 };
  const key = [
    anchor.track,
    anchor.x,
    anchor.z,
    anchor.bearing,
    renderer.store.topology?.epoch,
    renderer.store.topology?.revision,
  ].join(":");
  renderer.signalLanes ||= new Map();
  const old = renderer.signalLanes.get(signal.id);
  if (old?.key === key) return old;
  const nx = Math.cos(anchor.bearing),
    nz = -Math.sin(anchor.bearing),
    reach = 60;
  const bounds = [
    anchor.x - reach,
    anchor.z - reach,
    anchor.x + reach,
    anchor.z + reach,
  ];
  const tracks =
    renderer.index?.all?.length === renderer.store.tracks.size
      ? renderer.index.query(bounds).map((p) => renderer.store.tracks.get(p.id))
      : renderer.store.tracks.values();
  let gap = Infinity;
  for (const track of tracks) {
    if (track.id === anchor.track) continue;
    const p = track.points;
    for (let i = 2; i < p.length; i += 2) {
      const dx = p[i] - p[i - 2],
        dz = p[i + 1] - p[i - 1];
      if (Math.abs(dx * nx + dz * nz) > Math.hypot(dx, dz) * 0.55) continue;
      const hit = intersection(
        [anchor.x, anchor.z],
        [anchor.x + nx * reach, anchor.z + nz * reach],
        [p[i - 2], p[i - 1]],
        [p[i], p[i + 1]],
      );
      if (hit?.u > 1e-5) gap = Math.min(gap, hit.u * reach);
    }
  }
  const lane = { key, gap, corridor: Math.min(2, gap * 0.3) };
  renderer.signalLanes.set(signal.id, lane);
  return lane;
}

// Interpolate only the explicitly assigned rail at its native span. Never
// choose a nearby parallel track from geography.
export function signalAnchor(signal, store) {
  signal = signalMovement(signal, store);
  const track = store.tracks.get(signal.track),
    p = track?.points,
    spans = track?.spans;
  if (
    !p ||
    p.length < 4 ||
    !spans ||
    spans.length !== p.length / 2 ||
    !Number.isFinite(signal.span)
  )
    return null;
  let i = 0;
  while (i + 2 < spans.length && spans[i + 1] < signal.span) i++;
  const u = Math.min(
    1,
    Math.max(0, (signal.span - spans[i]) / (spans[i + 1] - spans[i] || 1)),
  );
  const bearing = signalBearing(signal, track);
  if (!Number.isFinite(bearing)) return null;
  return {
    track: signal.track,
    x: p[2 * i] + (p[2 * i + 2] - p[2 * i]) * u,
    z: p[2 * i + 1] + (p[2 * i + 3] - p[2 * i + 1]) * u,
    bearing,
  };
}

export function* prepareSignalEntriesWork(renderer, entries) {
  let processed = 0;
  for (const entry of entries) {
    entry.anchor = signalAnchor(entry.signal, renderer.store);
    entry.reference = renderer.scale;
  }
  groupSignalEntries(entries);
  for (const entry of entries) {
    const { signal: s, shape } = entry;
    const anchor = entry.anchor;
    const physical = { x: s.x * renderer.scale, y: -s.z * renderer.scale };
    let x = physical.x,
      y = physical.y,
      tx = 1,
      ty = 0,
      nx = 0,
      ny = 1;
    if (anchor) {
      tx = Math.sin(anchor.bearing);
      ty = -Math.cos(anchor.bearing);
      nx = -ty;
      ny = tx;
      const ax = anchor.x * renderer.scale,
        ay = -anchor.z * renderer.scale;

      // Native span is the attachment, travel defines right. Neither lamp
      // facing yaw nor physical mast offset can change that side.
      const lane = signalLane(renderer, s);
      const available = Math.max(
        0.15,
        (lane.gap - (2 * entry.groupHalfWidth) / renderer.scale - 0.35) / 2,
      );
      const corridor =
        Math.min((2 * preferences.trainScale) / 100, lane.corridor, available) *
        renderer.scale;
      const edge = entry.groupHalfWidth + corridor;
      entry.outX = nx;
      entry.outY = ny;
      entry.railX = ax;
      entry.railY = ay;
      entry.railTrack = anchor.track;
      entry.corridor = corridor;
      entry.clearance = edge;
      x = ax + nx * edge + entry.stackX - entry.groupCentreX;
      y = ay + ny * edge + entry.stackY - entry.groupCentreY;
      shape.anchorX = ax - physical.x;
      shape.anchorY = ay - physical.y;
    }
    // Keep the head at its own rail/physical position. Repeated collision
    // searches used to jump heads along the rail and create long connectors.

    if (++processed % 32 === 0) yield;
    entry.baseX = x;
    entry.baseY = y;
    shape.offsetX = x - physical.x;
    shape.offsetY = y - physical.y;
    shape.crowdingOpacity = 1;
  }
}
export function prepareSignalEntries(renderer, entries) {
  for (const step of prepareSignalEntriesWork(renderer, entries)) void step;
}
export function* signalPlacementInput(renderer, entries) {
  let processed = 0;
  const lod = signalLod(renderer);
  yield* prepareSignalEntriesWork(renderer, entries);
  if (lod.detail > 0) {
    const rails = [];
    if (!entries.length) return;
    const margin = Math.max(
      ...entries.map((e) =>
        Math.max(
          384 * Math.max(1, e.shape.scale),
          3 *
            (e.shape.width +
              e.shape.height +
              (e.shape.collisionExtraHeight || 0)),
        ),
      ),
    );
    const bounds = [
      Math.min(...entries.map((e) => e.baseX)) - margin,
      Math.min(...entries.map((e) => e.baseY)) - margin,
      Math.max(...entries.map((e) => e.baseX)) + margin,
      Math.max(...entries.map((e) => e.baseY)) + margin,
    ];
    const tracks =
      renderer.index?.all?.length === renderer.store.tracks.size
        ? renderer.index
            .query([
              bounds[0] / renderer.scale,
              -bounds[3] / renderer.scale,
              bounds[2] / renderer.scale,
              -bounds[1] / renderer.scale,
            ])
            .map((p) => renderer.store.tracks.get(p.id))
        : renderer.store.tracks.values();
    for (const track of tracks) {
      if (++processed % 64 === 0) yield;
      const points = track?.points || [];
      for (let i = 2; i < points.length; i += 2) {
        const a = [
            points[i - 2] * renderer.scale,
            -points[i - 1] * renderer.scale,
          ],
          b = [points[i] * renderer.scale, -points[i + 1] * renderer.scale];
        if (
          Math.max(a[0], b[0]) < bounds[0] ||
          Math.max(a[1], b[1]) < bounds[1] ||
          Math.min(a[0], b[0]) > bounds[2] ||
          Math.min(a[1], b[1]) > bounds[3]
        )
          continue;
        rails.push([a, b, 0, track.id]);
      }
    }
    // Static rail clearance; the owned rail separately reserves its vehicle
    // corridor. Reserving every neighbouring train's envelope made narrow yards
    // impossible even when no train was present.
    const clearance = 0.35 * renderer.scale;
    for (const rail of rails) rail[2] = clearance;
    const obstacles = [];
    if (renderer.layers?.switches)
      for (const j of renderer.store.junctions?.values() || []) {
        const size = renderer.junctionGeometry
          ? junctionBadge(renderer, j).width
          : (25 * preferences.switchScale) / 100;
        const radius = (size * Math.SQRT2) / 2 + 4;
        obstacles.push({
          x: j.x * renderer.scale - radius,
          y: -j.z * renderer.scale - radius,
          w: radius * 2,
          h: radius * 2,
        });
      }
    if (renderer.layers?.turntables)
      for (const t of renderer.store.tableDefs?.values() || []) {
        const radius = Math.max(9, t.radius * renderer.scale) + 4;
        obstacles.push({
          x: t.x * renderer.scale - radius,
          y: -t.z * renderer.scale - radius,
          w: radius * 2,
          h: radius * 2,
        });
      }
    if (renderer.layers?.signs)
      for (const s of renderer.store.signs?.values() || []) {
        const radius = (11 * preferences.signScale) / 100;
        const anchor = signalAnchor(s, renderer.store);
        const offset = ((9 + 3) * preferences.signScale) / 100;
        const x = anchor
          ? anchor.x * renderer.scale + Math.cos(anchor.bearing) * offset
          : s.x * renderer.scale;
        const y = anchor
          ? -anchor.z * renderer.scale + Math.sin(anchor.bearing) * offset
          : -s.z * renderer.scale;
        obstacles.push({
          x: x - radius,
          y: y - radius,
          w: radius * 2,
          h: radius * 2,
        });
      }
    return { rails, obstacles };
  }
}
