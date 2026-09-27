import { signalFootprint, boxSupport } from "./signal-footprint.js";
// A placement plan owns separating planes in world space. Zoom changes only
// marker footprints; pan, selection, blink and viewport membership never repack.
function cells(b, size = 128) {
  const keys = [];
  for (let x = Math.floor(b.x / size); x <= Math.floor((b.x + b.w) / size); x++)
    for (
      let y = Math.floor(b.y / size);
      y <= Math.floor((b.y + b.h) / size);
      y++
    )
      keys.push(x + ":" + y);
  return keys;
}
const intersects = (a, b) =>
  a.x < b.x + b.w && a.x + a.w > b.x && a.y < b.y + b.h && a.y + a.h > b.y;
const support = (n, box) => boxSupport(box, n[0], n[1]);
const dot = (n, p) => n[0] * p[0] + n[1] * p[1];
function separatingPlane(node, obstacle) {
  const axes = [
    [1, 0],
    [0, 1],
  ];
  for (const angle of [node.angle, obstacle.angle])
    if (angle) {
      axes.push(
        [Math.cos(angle), Math.sin(angle)],
        [-Math.sin(angle), Math.cos(angle)],
      );
    }
  if (obstacle.segment) {
    const [a, b] = obstacle.segment,
      dx = b[0] - a[0],
      dy = b[1] - a[1],
      len = Math.hypot(dx, dy);
    if (len) axes.push([-dy / len, dx / len]);
  }
  let best = null;
  for (const axis of axes)
    for (const sign of [-1, 1]) {
      const n = axis.map((v) => v * sign),
        own = dot(n, node.start) - support(n, node);
      const edge = obstacle.segment
        ? Math.max(...obstacle.segment.map((p) => dot(n, p))) +
          (obstacle.radius || 0)
        : dot(n, obstacle.start) + support(n, obstacle);
      const gap = own - edge;
      if (gap >= -1e-7 && (!best || gap > best.gap)) best = { n, edge, gap };
    }
  return best;
}
function clip(poly, n, edge) {
  const output = [];
  for (let i = 0; i < poly.length; i++) {
    const a = poly[i],
      b = poly[(i + 1) % poly.length],
      da = dot(n, a) - edge,
      db = dot(n, b) - edge;
    if (da >= -1e-8) output.push(a);
    if (da < 0 !== db < 0) {
      const u = da / (da - db);
      output.push([a[0] + u * (b[0] - a[0]), a[1] + u * (b[1] - a[1])]);
    }
  }
  return output;
}
function nearest(poly, target) {
  if (!poly.length) return null;
  let best = null,
    distance = Infinity;
  for (let i = 0; i < poly.length; i++) {
    const a = poly[i],
      b = poly[(i + 1) % poly.length],
      dx = b[0] - a[0],
      dy = b[1] - a[1];
    const u = Math.max(
      0,
      Math.min(
        1,
        ((target[0] - a[0]) * dx + (target[1] - a[1]) * dy) /
          (dx * dx + dy * dy || 1),
      ),
    );
    const p = [a[0] + u * dx, a[1] + u * dy],
      d = Math.hypot(p[0] - target[0], p[1] - target[1]);
    if (d < distance) {
      distance = d;
      best = p;
    }
  }
  return best;
}
export function makePlacementPlan(entries, reference, rails, obstacles = []) {
  const nodes = entries.map((e) => {
    const footprint = signalFootprint(e.shape),
      { w, h } = footprint;
    const target = [
      Number.isFinite(e.railX)
        ? e.railX + e.outX * e.corridor
        : e.signal.x * reference,
      Number.isFinite(e.railY)
        ? e.railY + e.outY * e.corridor
        : -e.signal.z * reference,
    ];
    const start = [
      e.signal.x * reference + e.shape.offsetX + footprint.cx,
      -e.signal.z * reference + e.shape.offsetY + footprint.cy,
    ];
    const x = Math.min(start[0], target[0]) - 1,
      y = Math.min(start[1], target[1]) - 1;
    const bounds = {
      x,
      y,
      w: Math.abs(start[0] - target[0]) + 2,
      h: Math.abs(start[1] - target[1]) + 2,
    };
    return {
      id: e.signal.id,
      members: e.members,
      groupKey: e.groupKey,
      start,
      target,
      w,
      h,
      angle: footprint.angle,
      localWidth: footprint.localWidth,
      localHeight: footprint.localHeight,
      detailZoom: e.shape.detailZoom || reference,
      center: [footprint.cx, footprint.cy],
      bounds,
      sweep: { x: x - w / 2, y: y - h / 2, w: bounds.w + w, h: bounds.h + h },
      planes: Number.isFinite(e.railX)
        ? [
            {
              n: [e.outX, e.outY],
              edge: e.outX * e.railX + e.outY * e.railY + e.corridor,
            },
          ]
        : [],
      pairs: [],
    };
  });
  const bins = new Map();
  const add = (b, obj) => {
    for (const key of cells(b)) {
      if (!bins.has(key)) bins.set(key, new Set());
      bins.get(key).add(obj);
    }
  };
  for (const [a, b, radius = 0] of rails) {
    const obstacle = { segment: [a, b], radius };
    const steps = Math.max(
      1,
      Math.ceil(Math.hypot(b[0] - a[0], b[1] - a[1]) / 64),
    );
    for (let i = 0; i < steps; i++) {
      const x0 = a[0] + ((b[0] - a[0]) * i) / steps,
        y0 = a[1] + ((b[1] - a[1]) * i) / steps,
        x1 = a[0] + ((b[0] - a[0]) * (i + 1)) / steps,
        y1 = a[1] + ((b[1] - a[1]) * (i + 1)) / steps;
      add(
        {
          x: Math.min(x0, x1) - radius,
          y: Math.min(y0, y1) - radius,
          w: Math.abs(x1 - x0) + radius * 2,
          h: Math.abs(y1 - y0) + radius * 2,
        },
        obstacle,
      );
    }
  }
  for (const b of obstacles)
    add(b, { start: [b.x + b.w / 2, b.y + b.h / 2], w: b.w, h: b.h });
  let unresolved = 0;
  for (const node of nodes) {
    const seen = new Set();
    for (const key of cells(node.sweep))
      for (const obstacle of bins.get(key) || []) {
        if (seen.has(obstacle)) continue;
        seen.add(obstacle);
        const p = separatingPlane(node, obstacle);
        if (p) node.planes.push(p);
        else unresolved++;
      }
  }
  bins.clear();
  for (const [i, node] of nodes.entries()) {
    const seen = new Set();
    for (const key of cells(node.sweep))
      for (const j of bins.get(key) || []) {
        if (seen.has(j)) continue;
        seen.add(j);
        const other = nodes[j];
        if (!intersects(node.sweep, other.sweep)) continue;
        const p = separatingPlane(node, other);
        if (!p) {
          unresolved++;
          continue;
        }
        node.pairs.push({ other: j, n: p.n });
        other.pairs.push({ other: i, n: p.n.map((v) => -v) });
      }
    add(node.sweep, i);
  }
  for (const node of nodes) {
    const planes = new Map();
    for (const p of node.planes) {
      const key = p.n.join(":");
      if (!planes.has(key) || planes.get(key).edge < p.edge) planes.set(key, p);
    }
    node.planes = [...planes.values()];
    const groups = new Map();
    for (const p of node.pairs) {
      const key = p.n.join(":");
      if (!groups.has(key)) groups.set(key, { n: p.n, items: [] });
      groups.get(key).items.push({
        other: p.other,
      });
    }
    node.pairGroups = [...groups.values()];
  }
  const visited = new Set();
  for (let i = 0; i < nodes.length; i++)
    if (!visited.has(i)) {
      const component = [],
        pending = [i];
      visited.add(i);
      while (pending.length) {
        const j = pending.pop();
        component.push(j);
        for (const p of nodes[j].pairs)
          if (!visited.has(p.other)) {
            visited.add(p.other);
            pending.push(p.other);
          }
      }
      component.sort((a, b) => a - b);
      for (const j of component) nodes[j].component = component;
    }
  return {
    reference,
    nodes,
    unresolved,
    byId: new Map(
      nodes.flatMap((n) => (n.members || [{ id: n.id }]).map((m) => [m.id, n])),
    ),
  };
}
export function projectPlacementPlan(
  plan,
  scale,
  component = plan.nodes.map((_, i) => i),
) {
  const ratios = {},
    positions = {};
  for (const i of component) {
    ratios[i] = Math.min(1, plan.nodes[i].detailZoom / scale);
    positions[i] = plan.nodes[i].start.slice();
  }
  // Every step remains inside the same feasible cell. The clipping planes and
  // their projection vary continuously with zoom; there is no lane reordering.
  // The solver already produced valid positions at the reference size. Running
  // clipping again there can move a retained head after a neighbouring addition.
  for (let pass = 0; scale > plan.reference && pass < 3; pass++)
    for (const i of component) {
      const node = plan.nodes[i];
      const ratio = ratios[i];
      const b = node.bounds;
      let poly = [
        [b.x, b.y],
        [b.x + b.w, b.y],
        [b.x + b.w, b.y + b.h],
        [b.x, b.y + b.h],
      ];
      const constraints = node.planes.map((p) => ({
        n: p.n,
        edge: p.edge + support(p.n, node) * ratio,
      }));
      // Parallel half-planes are exactly equivalent to their strongest bound.
      // Evaluate neighbour limits without repeatedly clipping the same axis.
      for (const group of node.pairGroups) {
        let edge = -Infinity;
        for (const p of group.items)
          edge = Math.max(
            edge,
            dot(group.n, positions[p.other]) +
              support(group.n, node) * ratio +
              support(group.n, plan.nodes[p.other]) * ratios[p.other],
          );
        constraints.push({ n: group.n, edge });
      }
      const target = node.target;
      const fits =
        target[0] >= b.x &&
        target[0] <= b.x + b.w &&
        target[1] >= b.y &&
        target[1] <= b.y + b.h &&
        constraints.every((p) => dot(p.n, target) >= p.edge - 1e-8);
      if (!fits) for (const p of constraints) poly = clip(poly, p.n, p.edge);
      const point = fits ? target : nearest(poly, target);
      if (point) positions[i] = point;
    }
  return new Map(
    component.flatMap((i) => {
      const n = plan.nodes[i];
      const ratio = ratios[i];
      const blend = 1 - Math.min(1, plan.reference / scale);
      const position = positions[i].map(
        (v, k) => n.start[k] + (v - n.start[k]) * blend,
      );
      return (n.members || [{ id: n.id, x: 0, y: 0 }]).map((m) => [
        m.id,
        {
          x: (position[0] + (m.x - n.center[0]) * ratio) / plan.reference,
          z: -(position[1] + (m.y - n.center[1]) * ratio) / plan.reference,
        },
      ]);
    }),
  );
}
