const compareId = (a, b) => (a < b ? -1 : a > b ? 1 : 0);
import {
  signalPlacementInput,
  prepareSignalEntriesWork,
} from "./signal-placement.js";
import {
  makePlacementPlan,
  projectPlacementPlan,
} from "./signal-placement-plan.js";
import { separateSignalBoxes } from "./signal-collisions.js";
import { signalFootprint } from "./signal-footprint.js";
import { packSignalGroups } from "./signal-groups.js";

// Partition by the complete area a marker can reach, not by viewport or a
// station-name guess. Disjoint envelopes cannot collide even after placement.
// Streaming one yard consequently cannot repack unrelated yards.
const overlap = (a, b) =>
  a[0] <= b[2] && a[2] >= b[0] && a[1] <= b[3] && a[3] >= b[1];
function envelope(e, reference) {
  const s = e.shape,
    { w, h } = signalFootprint(s),
    reach = Math.max(384 * Math.max(1, s.scale), 3 * Math.max(w, h)),
    radius = reach + Math.max(w, h),
    px = e.signal.x * reference,
    py = -e.signal.z * reference;
  return [
    Math.min(e.baseX, px) - radius,
    Math.min(e.baseY, py) - radius,
    Math.max(e.baseX, px) + radius,
    Math.max(e.baseY, py) + radius,
  ];
}
function keys(b) {
  const result = [];
  for (let x = Math.floor(b[0] / 512); x <= Math.floor(b[2] / 512); x++)
    for (let y = Math.floor(b[1] / 512); y <= Math.floor(b[3] / 512); y++)
      result.push(x + ":" + y);
  return result;
}
export function* updateSignalRegions(renderer, entries, reference, previous) {
  const view = Object.create(renderer);
  view.scale = reference;
  if (
    previous?.reference === reference &&
    previous.signRevision === renderer.store.signGeometryRevision &&
    entries.length >= previous.entryKeys.size
  ) {
    const added = [];
    let stable = 0;
    for (const e of entries) {
      if (previous.entryKeys.get(e.signal.id) === e.key) stable++;
      else added.push(e);
    }
    if (stable === previous.entryKeys.size) {
      yield* prepareSignalEntriesWork(view, added);
      if (!added.length) return previous;
      const affected = new Set();
      for (const e of added)
        for (const region of previous.regions.values())
          if (overlap(envelope(e, reference), region.box)) affected.add(region);
      const local = added.slice();
      for (const region of affected)
        for (const id of region.members)
          local.push(previous.entriesById.get(id));
      const addition = yield* updateSignalRegions(
        renderer,
        local,
        reference,
        null,
      );
      for (const [id, region] of addition.byId) {
        const prior = previous.byId.get(id);
        if (prior) {
          region.prior ||= new Map();
          region.prior.set(id, prior);
        }
      }
      for (const region of affected) previous.regions.delete(region.id);
      for (const [id, region] of addition.regions)
        previous.regions.set(id, region);
      for (const [id, region] of addition.byId) previous.byId.set(id, region);
      for (const e of added) {
        previous.entryKeys.set(e.signal.id, e.key);
        previous.entriesById.set(e.signal.id, e);
      }
      return previous;
    }
  }
  yield* prepareSignalEntriesWork(view, entries);
  const parents = entries.map((_, i) => i),
    bins = new Map(),
    bounds = [];
  let operations = 0;
  const root = (i) => {
    while (parents[i] !== i) {
      parents[i] = parents[parents[i]];
      i = parents[i];
    }
    return i;
  };
  for (let i = 0; i < entries.length; i++) {
    const e = entries[i],
      b = envelope(e, reference);
    bounds.push(b);
    e.regionBounds = b;
    const cells = keys(b),
      seen = new Set();
    for (const key of cells)
      for (const j of bins.get(key) || []) {
        if (++operations % 1024 === 0) yield;
        if (seen.has(j)) continue;
        seen.add(j);
        if (root(i) !== root(j) && overlap(b, bounds[j]))
          parents[root(i)] = root(j);
      }
    for (const key of cells) {
      if (!bins.has(key)) bins.set(key, []);
      bins.get(key).push(i);
    }
    if (i % 64 === 63) yield;
  }
  const groups = new Map();
  entries.forEach((e, i) => {
    const key = root(i);
    if (!groups.has(key)) groups.set(key, []);
    groups.get(key).push(e);
  });
  const regions = new Map(),
    byId = new Map();
  for (const group of groups.values()) {
    group.sort(
      (a, b) =>
        compareId(
          a.signal.controller || a.signal.id,
          b.signal.controller || b.signal.id,
        ) ||
        (a.signal.displayOrder || 0) - (b.signal.displayOrder || 0) ||
        compareId(a.signal.id, b.signal.id),
    );
    const id = group.map((e) => e.signal.id).sort()[0];
    const box = [Infinity, Infinity, -Infinity, -Infinity];
    for (const e of group) {
      const b = e.regionBounds;
      box[0] = Math.min(box[0], b[0]);
      box[1] = Math.min(box[1], b[1]);
      box[2] = Math.max(box[2], b[2]);
      box[3] = Math.max(box[3], b[3]);
    }
    const signs = renderer.layers.signs
      ? [...renderer.store.signs.values()].filter((s) =>
          overlap(box, [
            s.x * reference,
            -s.z * reference,
            s.x * reference,
            -s.z * reference,
          ]),
        )
      : [];
    const key =
      group.map((e) => e.key).join("|") +
      ":" +
      signs
        .map((s) => [s.id, s.x, s.z].join(","))
        .sort()
        .join("|");
    const old = previous?.reference === reference && previous.regions.get(id);
    const region =
      old?.key === key
        ? old
        : {
            id,
            key,
            entries: group,
            entryMap: new Map(group.map((e) => [e.signal.id, e])),
            members: group.map((e) => e.signal.id),
            sources: new Map(group.map((e) => [e.signal.id, e.signal])),
            keys: new Map(group.map((e) => [e.signal.id, e.key])),
            prior: new Map(
              group
                .map((e) => [e.signal.id, previous?.byId.get(e.signal.id)])
                .filter(([, r]) => r),
            ),
            box,
            plan: null,
            positions: null,
          };
    regions.set(id, region);
    for (const e of group) byId.set(e.signal.id, region);
  }
  const result = previous || {};
  result.reference = reference;
  result.regions = regions;
  result.byId = byId;
  result.entryKeys = new Map(entries.map((e) => [e.signal.id, e.key]));
  result.signRevision = renderer.store.signGeometryRevision;
  result.entriesById = new Map(entries.map((e) => [e.signal.id, e]));
  Object.defineProperties(result, {
    nodes: {
      configurable: true,
      get: () => [...regions.values()].flatMap((r) => r.plan?.nodes || []),
    },
    unresolved: {
      configurable: true,
      get: () =>
        [...regions.values()].reduce(
          (n, r) => n + (r.plan?.unresolved || 0),
          0,
        ),
    },
  });
  return result;
}
export function regionPosition(renderer, signal) {
  const manager = renderer.signalPlacementPlan,
    region = manager.byId.get(signal.id);
  if (!region) return null;
  const source = manager.entriesById.get(signal.id)?.signal;
  if (
    source &&
    (source.x !== signal.x ||
      source.z !== signal.z ||
      source.track !== signal.track ||
      source.span !== signal.span)
  )
    return null;
  if (!region.plan) {
    const view = Object.create(renderer);
    view.scale = manager.reference;
    if (!region.pending && !region.preparing) {
      region.preparing = true;
      const work = prepareRegion(renderer, manager, region, view);
      if (renderer.placementQueue?.worker) {
        renderer.signalInputWork ||= renderer.placementQueue.preparing ||=
          new Map();
        renderer.signalInputWork.set(region, work);
      } else for (const step of work) void step;
    }
    if (!region.plan) {
      const entry = region.entryMap.get(signal.id);
      let prior = region.prior?.get(signal.id);
      const seen = new Set();
      while (prior && !prior.plan && !seen.has(prior)) {
        seen.add(prior);
        prior = prior.prior?.get(signal.id);
      }
      const old = prior?.sources?.get(signal.id);
      if (
        prior?.plan &&
        prior.plan.byId.get(signal.id)?.groupKey === entry.groupKey &&
        old?.x === signal.x &&
        old?.z === signal.z &&
        old?.track === signal.track &&
        old?.span === signal.span
      )
        return {
          region,
          plan: null,
          position: projectRegion(prior, renderer.scale, signal.id),
        };
      return {
        region,
        plan: null,
        position: {
          x: entry.baseX / manager.reference,
          z: -entry.baseY / manager.reference,
        },
      };
    }
  }
  return {
    position: projectRegion(region, renderer.scale, signal.id),
    region,
    plan: region.plan,
  };
}
function projectRegion(region, zoom, id) {
  const scale = Math.max(zoom, region.plan.reference);
  if (region.scale !== scale) {
    region.scale = scale;
    region.positions =
      scale === region.plan.reference && region.plan.referencePositions
        ? region.plan.referencePositions
        : new Map();
  }
  if (!region.positions.has(id)) {
    const node = region.plan.byId.get(id);
    for (const [key, p] of projectPlacementPlan(
      region.plan,
      zoom,
      node.component,
    ))
      region.positions.set(key, p);
  }
  return region.positions.get(id);
}

function* prepareRegion(renderer, manager, region, view) {
  try {
    const useWorker = !!renderer.placementQueue?.worker;
    const { rails = [], obstacles = [] } =
      (yield* signalPlacementInput(view, region.entries)) || {};
    if (renderer.signalPlacementPlan?.regions.get(region.id) !== region) return;
    for (const entry of region.entries) {
      const prior = region.prior?.get(entry.signal.id);
      if (
        prior?.plan &&
        prior.keys.get(entry.signal.id) === entry.key &&
        prior.plan.byId.get(entry.signal.id)?.groupKey === entry.groupKey
      ) {
        const position = projectRegion(
          prior,
          manager.reference,
          entry.signal.id,
        );
        entry.priorX = position.x * manager.reference - entry.baseX;
        entry.priorY = -position.z * manager.reference - entry.baseY;
      }
    }
    const entries = region.entries.map((e) => ({
      signal: {
        id: e.signal.id,
        controller: e.signal.controller,
        displayOrder: e.signal.displayOrder,
        x: e.signal.x,
        z: e.signal.z,
      },
      shape: {
        width: e.shape.width,
        height: e.shape.height,
        angle: e.shape.angle,
        bearing: e.shape.bearing,
        detailZoom: e.shape.detailZoom,
        scale: e.shape.scale,
        collisionExtraHeight: e.shape.collisionExtraHeight,
        collisionMinWidth: e.shape.collisionMinWidth,
        offsetX: e.shape.offsetX,
        offsetY: e.shape.offsetY,
      },
      baseX: e.baseX,
      baseY: e.baseY,
      groupId: e.groupId,
      groupKey: e.groupKey,
      groupIndex: e.groupIndex,
      reference: e.reference,
      outX: e.outX,
      outY: e.outY,
      railX: e.railX,
      railY: e.railY,
      railTrack: e.railTrack,
      clearance: e.clearance,
      corridor: e.corridor,
      priorX: e.priorX,
      priorY: e.priorY,
    }));
    if (
      !useWorker ||
      !renderer.placementQueue.submit(region, {
        entries,
        reference: manager.reference,
        rails,
        obstacles,
      })
    ) {
      const packed = packSignalGroups(region.entries);
      separateSignalBoxes(packed, rails, obstacles);
      region.plan = makePlacementPlan(
        packed,
        manager.reference,
        rails,
        obstacles,
      );
      region.entries = null;
    }
  } finally {
    region.preparing = false;
  }
}
