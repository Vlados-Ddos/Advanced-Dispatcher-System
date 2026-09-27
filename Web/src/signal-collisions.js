const compareId = (a, b) => (a < b ? -1 : a > b ? 1 : 0);
import {
  signalFootprint,
  boxSupport,
  boxesOverlap,
} from "./signal-footprint.js";
import { intersection } from "./track-geometry.js";
// Screen-space boxes include the whole signal and its own indicator stack.
// Candidate axes come from the regulated right side/track tangent. Nothing in
// this module changes track ownership, direction, aspect or route matching.
export function separateSignalBoxes(entries, rails = [], obstacles = []) {
  const railBins = new Map(),
    railCell = 128;
  for (const segment of rails) {
    const [a, b] = segment,
      steps = Math.max(
        1,
        Math.ceil(Math.hypot(b[0] - a[0], b[1] - a[1]) / railCell),
      );
    for (let i = 0; i <= steps; i++) {
      const x = Math.floor((a[0] + ((b[0] - a[0]) * i) / steps) / railCell),
        y = Math.floor((a[1] + ((b[1] - a[1]) * i) / steps) / railCell);
      for (let dx = -1; dx <= 1; dx++)
        for (let dy = -1; dy <= 1; dy++) {
          const key = x + dx + ":" + (y + dy);
          if (!railBins.has(key)) railBins.set(key, new Set());
          railBins.get(key).add(segment);
        }
    }
  }
  const crossesRail = (box) => {
    const nearby = new Set();
    for (
      let x = Math.floor(box.x / railCell);
      x <= Math.floor((box.x + box.w) / railCell);
      x++
    )
      for (
        let y = Math.floor(box.y / railCell);
        y <= Math.floor((box.y + box.h) / railCell);
        y++
      )
        for (const segment of railBins.get(x + ":" + y) || [])
          nearby.add(segment);
    return [...nearby].some(([start, end, radius = 0]) => {
      const cx = box.x + box.w / 2,
        cy = box.y + box.h / 2,
        angle = box.angle || 0;
      const c = Math.cos(angle),
        s = Math.sin(angle);
      const local = (p) => [
        (p[0] - cx) * c + (p[1] - cy) * s,
        -(p[0] - cx) * s + (p[1] - cy) * c,
      ];
      const a = local(start),
        b = local(end),
        w = (box.localWidth ?? box.w) / 2,
        h = (box.localHeight ?? box.h) / 2;
      // Liang–Barsky segment/rectangle clipping; real topology, not proximity.
      let lo = 0,
        hi = 1;
      for (const [axis, min, max] of [
        [0, -w - radius, w + radius],
        [1, -h - radius, h + radius],
      ]) {
        const d = b[axis] - a[axis];
        if (Math.abs(d) < 1e-9) {
          if (a[axis] < min || a[axis] > max) return false;
        } else {
          const t0 = (min - a[axis]) / d,
            t1 = (max - a[axis]) / d;
          lo = Math.max(lo, Math.min(t0, t1));
          hi = Math.min(hi, Math.max(t0, t1));
          if (lo > hi) return false;
        }
      }
      return true;
    });
  };
  const bins = new Map(),
    cell = 64;
  const keys = (box) => {
    const result = [];
    for (
      let x = Math.floor(box.x / cell);
      x <= Math.floor((box.x + box.w) / cell);
      x++
    )
      for (
        let y = Math.floor(box.y / cell);
        y <= Math.floor((box.y + box.h) / cell);
        y++
      )
        result.push(x + ":" + y);
    return result;
  };
  for (const box of obstacles)
    for (const key of keys(box)) {
      if (!bins.has(key)) bins.set(key, []);
      bins.get(key).push(box);
    }
  const collides = (box) => {
    if (crossesRail(box)) return true;
    for (const key of keys(box))
      for (const b of bins.get(key) || [])
        if (boxesOverlap(box, b)) return true;
    return false;
  };
  const ordered = [...entries].sort(
    (a, b) =>
      Number(Number.isFinite(b.priorX)) - Number(Number.isFinite(a.priorX)) ||
      compareId(a.railTrack || "", b.railTrack || "") ||
      (a.railX || 0) * -(a.outY || 0) +
        (a.railY || 0) * (a.outX || 0) -
        ((b.railX || 0) * -(b.outY || 0) + (b.railY || 0) * (b.outX || 0)) ||
      compareId(
        a.signal.controller || a.signal.id,
        b.signal.controller || b.signal.id,
      ) ||
      (a.signal.displayOrder || 0) - (b.signal.displayOrder || 0) ||
      compareId(a.signal.id, b.signal.id),
  );
  for (const e of ordered) {
    const s = e.shape,
      footprint = signalFootprint(s),
      { w, h } = footprint,
      baseX = e.baseX,
      baseY = e.baseY,
      boxAt = (dx, dy) => ({
        x: baseX + dx + footprint.cx - w / 2,
        y: baseY + dy + footprint.cy - h / 2,
        w,
        h,
        angle: footprint.angle,
        localWidth: footprint.localWidth,
        localHeight: footprint.localHeight,
      });
    const crossesNeighbour = (box) => {
      if (!Number.isFinite(e.railX)) return false;
      const a = [e.railX, e.railY],
        b = [box.x + box.w / 2, box.y + box.h / 2];
      const bounds = {
        x: Math.min(a[0], b[0]),
        y: Math.min(a[1], b[1]),
        w: Math.abs(b[0] - a[0]),
        h: Math.abs(b[1] - a[1]),
      };
      const nearby = new Set();
      for (
        let x = Math.floor(bounds.x / railCell);
        x <= Math.floor((bounds.x + bounds.w) / railCell);
        x++
      )
        for (
          let y = Math.floor(bounds.y / railCell);
          y <= Math.floor((bounds.y + bounds.h) / railCell);
          y++
        )
          for (const rail of railBins.get(x + ":" + y) || []) nearby.add(rail);
      return [...nearby].some(
        ([c, d, , id]) =>
          id !== e.railTrack &&
          Math.abs(
            (d[0] - c[0]) * (e.outX || 0) + (d[1] - c[1]) * (e.outY || 0),
          ) <
            Math.hypot(d[0] - c[0], d[1] - c[1]) * 0.55 &&
          intersection(a, b, c, d)?.u > 1e-4,
      );
    };
    let dx = e.priorX || 0,
      dy = e.priorY || 0,
      box = boxAt(dx, dy);
    if (collides(box)) {
      // Only the right side of the owned rail. Prefer compact longitudinal
      // staggering over walking across successive parallel tracks.
      const nx = e.outX ?? 0,
        ny = e.outY ?? -1,
        tx = -ny,
        ty = nx;
      const normalStep = 2 * boxSupport(footprint, nx, ny),
        tangentStep = 2 * boxSupport(footprint, tx, ty);
      const railX = e.railX ?? baseX,
        railY = e.railY ?? baseY;
      const edge = e.clearance ?? normalStep / 2;
      const candidates = [];
      for (let n = 0; n <= 3; n++)
        for (let t = -3; t <= 3; t += 0.5) {
          const normal = edge + n * normalStep;
          const x =
              railX + nx * normal + tx * t * tangentStep - baseX - footprint.cx,
            y =
              railY + ny * normal + ty * t * tangentStep - baseY - footprint.cy;
          const distance = Math.hypot(normal, t * tangentStep);
          candidates.push({
            x,
            y,
            cost: distance + n * normalStep * 2 + Math.abs(t) * 2 * s.scale,
          });
        }
      candidates.sort((a, b) => a.cost - b.cost || a.x - b.x || a.y - b.y);
      for (const c of candidates) {
        const next = boxAt(c.x, c.y);
        if (!collides(next) && !crossesNeighbour(next)) {
          dx = c.x;
          dy = c.y;
          box = next;
          break;
        }
      }
    }
    for (const key of keys(box)) {
      if (!bins.has(key)) bins.set(key, []);
      bins.get(key).push(box);
    }
    s.offsetX += dx;
    s.offsetY += dy;
  }
}
