import { aboveLod } from "./lod.js";
import { updateSignalRegions, regionPosition } from "./signal-regions.js";
import { signalLod, signalDetailScale } from "./signal-lod.js";
import { junctionLod } from "./junction-lod.js";
import { signalAnchor, prepareSignalEntries } from "./signal-placement.js";
import { railwaySigns, signVisible, isSignalBoard } from "./railway-objects.js";
import { mechanicalFace } from "./mechanical-signals.js";
import { signalComposite, signalParts } from "./signal-composite.js";
import { allLocations, locationVisible } from "./locations.js";
import { lampFace } from "./signal-geometry.js";
import { normalizeSignalFace } from "./signal-metrics.js";
import { signalPositionKey } from "./signal-groups.js";
import { preferences, mapMetrics } from "./preferences.js";
import {
  signalBearing,
  signalVisible,
  signalMovement,
} from "./signal-display.js";

function geometryKey(renderer, s) {
  const movement = signalMovement(s, renderer.store),
    anchor = signalAnchor(s, renderer.store);
  return JSON.stringify([
    s.id,
    s.x,
    s.z,
    s.controller,
    s.displayOrder,
    s.track,
    s.span,
    s.visualKind,
    s.shuntingSignal,
    s.classificationKnown,
    movement.track,
    movement.direction,
    movement.span,
    anchor?.x,
    anchor?.z,
    signalVisible(s, renderer.layers),
    s.lampLayout?.length
      ? s.lampLayout.map((l) => [l.id, l.x, l.y])
      : s.lamps?.length,
    signalParts(s).map((p) => [p.id, p.kind, p.order]),
    (s.parts || []).filter(isSignalBoard).map((p) => [p.id, p.order]),
  ]);
}
function planSignature(renderer) {
  return [
    preferences.signalScale,
    preferences.indicatorScale,
    preferences.signScale,
    preferences.switchScale,
    preferences.trainScale,
    renderer.layers.signals,
    renderer.layers.shuntingSignals,
    renderer.layers.additionalSigns,
    renderer.layers.switches,
    renderer.layers.signs,
    renderer.layers.turntables,
    renderer.store.topology?.epoch,
    renderer.store.topology?.revision,
  ].join(":");
}
function* prepareSignalPlan(renderer, signature, revision) {
  let placementChanged = false;
  const source = renderer.store.signals,
    sources = [...source.values()];
  const signsRevision = renderer.store.signGeometryRevision;
  let processed = 0;
  let rebuild =
    !renderer.signalLayouts ||
    !renderer.signalPlacementPlan ||
    renderer.signalPlanSignature !== signature;
  if (
    rebuild ||
    renderer.signalGeometryRevision !== revision ||
    renderer.signalGeometrySource !== renderer.store.signals
  ) {
    const keys = new Map();
    renderer.signalGeometryCache ||= new WeakMap();
    for (const s of sources) {
      const block = renderer.store.blocks?.get(s.block);
      const movementKey = [block?.tracks?.[0], block?.directions?.[0]].join(
        ":",
      );
      const cached = renderer.signalGeometryCache.get(s);
      const version = renderer.store.signalVersions?.get(s.id);
      let key = cached?.key;
      if (
        key === undefined ||
        cached.movementKey !== movementKey ||
        cached.version !== version ||
        renderer.signalPlanSignature !== signature
      ) {
        key = geometryKey(renderer, s);
        renderer.signalGeometryCache.set(s, { key, movementKey, version });
      }
      keys.set(s.id, key);
      if (++processed % 128 === 0) yield;
      if (renderer.signalGeometryKeys?.get(s.id) !== key) rebuild = true;
    }
    if (keys.size !== renderer.signalGeometryKeys?.size) rebuild = true;
    if (
      signature !== planSignature(renderer) ||
      source !== renderer.store.signals
    )
      return;
    renderer.signalGeometryKeys = keys;
    renderer.signalGeometryRevision = revision;
    renderer.signalGeometrySource = renderer.store.signals;
  }
  if (
    rebuild ||
    renderer.signalPlanSignRevision !== renderer.store.signGeometryRevision
  ) {
    placementChanged = true;
    const reference = signalDetailScale,
      view = Object.create(renderer);
    view.scale = reference;
    const entries = [];
    renderer.signalEntryCache ||= new Map();
    for (const s of sources) {
      if (!signalVisible(s, renderer.layers)) continue;
      const key = renderer.signalGeometryKeys.get(s.id);
      let cached = renderer.signalEntryCache.get(s.id);
      if (
        !cached ||
        cached.key !== key ||
        renderer.signalPlanSignature !== signature
      ) {
        const shape = signalHeadLayout(view, s);
        if (renderer.layers.additionalSigns) {
          const boards = (s.parts || []).filter(isSignalBoard).length;
          shape.collisionExtraHeight =
            (boards * 24 * preferences.signScale) / 100;
          shape.collisionMinWidth = boards
            ? (18 * preferences.signScale) / 100
            : 0;
        }
        cached = { signal: s, shape, key };
        renderer.signalEntryCache.set(s.id, cached);
      }
      entries.push(cached);
      if (++processed % 128 === 0) yield;
    }
    for (const id of renderer.signalEntryCache.keys())
      if (!renderer.store.signals.has(id)) {
        renderer.signalEntryCache.delete(id);
        renderer.signalLayouts?.delete(id);
        renderer.signalLanes?.delete(id);
      }
    if (
      signature !== planSignature(renderer) ||
      source !== renderer.store.signals
    )
      return;
    renderer.signalPlacementPlan = yield* updateSignalRegions(
      renderer,
      entries,
      reference,
      renderer.signalPlanSignature === signature
        ? renderer.signalPlacementPlan
        : null,
    );
    renderer.signalPlanSignature = signature;
    renderer.initialSignalGroups = null;
    renderer.signalPlanSignRevision = signsRevision;
    renderer.signalLayouts ||= new Map();
  }
  if (placementChanged)
    renderer.infrastructureDirty = renderer.interactionDirty = true;
}
export function advanceSignalLayouts(renderer, budget = 2) {
  const started = performance.now();
  while (renderer.signalWork && performance.now() - started < budget) {
    if (renderer.signalWork.next().done) renderer.signalWork = null;
  }
  for (const [region, work] of renderer.signalInputWork || []) {
    if (renderer.signalPlacementPlan?.regions.get(region.id) !== region) {
      work.return();
      renderer.signalInputWork.delete(region);
      continue;
    }
    while (performance.now() - started < budget) {
      if (work.next().done) {
        renderer.signalInputWork.delete(region);
        break;
      }
    }
    if (performance.now() - started >= budget) break;
  }
  if (!renderer.signalInputWork?.size) renderer.placementQueue?.pump();
}
// Shared geometry, retained through lamp/aspect/indicator state changes.
function initialGroupPosition(renderer, signal, signature, revision) {
  let cache = renderer.initialSignalGroups;
  if (
    !cache ||
    cache.signature !== signature ||
    cache.revision !== revision ||
    cache.scale !== renderer.scale
  ) {
    cache = {
      signature,
      revision,
      scale: renderer.scale,
      positions: new Map(),
      groups: new Map(),
    };
    for (const s of renderer.store.signals.values())
      if (signalVisible(s, renderer.layers)) {
        const key = signalPositionKey(signalAnchor(s, renderer.store), s);
        if (!cache.groups.has(key)) cache.groups.set(key, []);
        cache.groups.get(key).push(s);
      }
    renderer.initialSignalGroups = cache;
  }
  if (!cache.positions.has(signal.id)) {
    const key = signalPositionKey(signalAnchor(signal, renderer.store), signal);
    const entries = (cache.groups.get(key) || [signal]).map((s) => {
      const shape = signalHeadLayout(renderer, s),
        boards = renderer.layers.additionalSigns
          ? (s.parts || []).filter(isSignalBoard).length
          : 0;
      shape.collisionExtraHeight =
        ((boards * 24 * preferences.signScale) / 100) *
        signalLod(renderer).size;
      shape.collisionMinWidth = boards
        ? ((18 * preferences.signScale) / 100) * signalLod(renderer).size
        : 0;
      return { signal: s, shape };
    });
    prepareSignalEntries(renderer, entries);
    for (const e of entries)
      cache.positions.set(e.signal.id, {
        offsetX: e.shape.offsetX,
        offsetY: e.shape.offsetY,
      });
  }
  return cache.positions.get(signal.id);
}
export function signalLayout(renderer, signal) {
  if (!signalLod(renderer, signal).visible) {
    const key = [
      renderer.scale,
      preferences.signalScale,
      preferences.indicatorScale,
      renderer.store.presentationRevision,
    ].join(":");
    if (renderer.signalOverviewKey !== key) {
      renderer.signalOverviewKey = key;
      renderer.signalOverviewLayouts = new Map();
    }
    const cached = renderer.signalOverviewLayouts.get(signal.id);
    if (cached?.signal === signal) return cached.shape;
    const shape = signalHeadLayout(renderer, signal),
      anchor = signalAnchor(signal, renderer.store);
    const result = {
      ...shape,
      offsetX: 0,
      offsetY: 0,
      anchorX: anchor ? (anchor.x - signal.x) * renderer.scale : 0,
      anchorY: anchor ? (signal.z - anchor.z) * renderer.scale : 0,
      crowdingOpacity: 1,
    };
    renderer.signalOverviewLayouts.set(signal.id, { signal, shape: result });
    return result;
  }
  const owner = renderer.layoutOwner || renderer;
  if (owner !== renderer) signalLayout(owner, signal);
  const signature = planSignature(renderer),
    revision = renderer.store.presentationRevision;
  if (
    owner === renderer &&
    !renderer.signalWork &&
    (!renderer.signalLayouts ||
      !renderer.signalPlacementPlan ||
      renderer.signalPlanSignature !== signature ||
      renderer.signalGeometryRevision !== revision ||
      renderer.signalPlanSignRevision !== renderer.store.signGeometryRevision ||
      renderer.signalGeometrySource !== renderer.store.signals)
  )
    renderer.signalWork = prepareSignalPlan(renderer, signature, revision);
  if (owner === renderer && !renderer.placementQueue?.worker)
    advanceSignalLayouts(renderer, Infinity);
  const formKey = [
    renderer.scale,
    preferences.signalScale,
    preferences.indicatorScale,
  ].join(":");
  if (renderer.signalFormKey !== formKey || !renderer.signalLayouts) {
    renderer.signalFormKey = formKey;
    renderer.signalShapeScale = renderer.scale;
    renderer.signalLayouts = new Map();
  }
  const placed = renderer.signalPlacementPlan
    ? regionPosition(renderer, signal)
    : null;
  let shape = renderer.signalLayouts.get(signal.id);
  const stateRevision =
    renderer.store.signalVersions?.get(signal.id) ?? revision;
  const geometry = renderer.signalGeometryKeys?.get(signal.id);
  const shapeChanged =
    !shape ||
    shape.signal !== signal ||
    shape.stateRevision !== stateRevision ||
    (shape.geometryKey !== undefined && shape.geometryKey !== geometry);
  if (shapeChanged) shape = signalHeadLayout(renderer, signal);
  if (
    shapeChanged ||
    shape.region !== placed?.region ||
    shape.placement !== placed?.plan ||
    shape.geometryKey !== renderer.signalGeometryKeys?.get(signal.id)
  ) {
    const position = placed?.position,
      anchor = signalAnchor(signal, renderer.store);
    shape.offsetX = position ? (position.x - signal.x) * renderer.scale : 0;
    shape.offsetY = position ? (signal.z - position.z) * renderer.scale : 0;
    if (!position)
      Object.assign(
        shape,
        initialGroupPosition(renderer, signal, signature, revision),
      );
    shape.anchorX = anchor ? (anchor.x - signal.x) * renderer.scale : 0;
    shape.anchorY = anchor ? (signal.z - anchor.z) * renderer.scale : 0;
    shape.crowdingOpacity = 1;
    shape.signal = signal;
    shape.region = placed?.region;
    shape.placement = placed?.plan;
    shape.stateRevision = stateRevision;
    shape.geometryKey = renderer.signalGeometryKeys?.get(signal.id);
    renderer.signalLayouts.set(signal.id, shape);
  }
  return shape;
}
export function signalHeadLayout(renderer, signal) {
  const movement = signalMovement(signal, renderer.store);
  const track = renderer.store.tracks.get(movement.track);
  const bearing = signalBearing(movement, track);
  const scale = (preferences.signalScale / 100) * signalLod(renderer).size;
  const radius = mapMetrics.signalRadius * scale;
  const nativeFace =
    mechanicalFace(signal, scale) || lampFace(signal, radius, scale);
  // Restore the pre-0.7.14 compact electrical shunting housing. The shared
  // scale, rail attachment and placement group do not depend on this design.
  const face =
    (signal.shuntingSignal || signal.visualKind === "shunting") &&
    nativeFace.drawingScale == null
      ? nativeFace
      : normalizeSignalFace(nativeFace, scale);
  const width = face.width;
  const height = face.height;
  const result = signalComposite(renderer, signal, {
    ...face,
    scale,
    width,
    height,
    bearing,
    angle: bearing ?? 0,
    left: -width / 2,
    top: -height / 2,
  });
  result.detailZoom = signalDetailScale;
  return result;
}
export function carLayout(renderer, car) {
  const length = car.length;
  const icon = preferences.trainScale / 100;
  const unit = Math.max(0.55, renderer.scale) * icon;
  return {
    width: Number.isFinite(length) && length > 0 ? length * unit : 8 * icon,
    height: Math.max(
      mapMetrics.minTrainWidth * icon,
      (car.width > 0 ? car.width : 3) * unit,
    ),
    angle: ((car.yaw - 90) * Math.PI) / 180,
  };
}

export function rectHit(x, y, shape, padding = 3) {
  const cos = Math.cos(shape.angle || 0),
    sin = Math.sin(shape.angle || 0);
  const lx = x * cos + y * sin,
    ly = -x * sin + y * cos;
  if (shape.diamond)
    return Math.abs(lx) + Math.abs(ly) <= shape.width / 2 + padding;
  const left = shape.left ?? -shape.width / 2,
    top = shape.top ?? -shape.height / 2;
  return (
    lx >= left - padding &&
    lx <= left + shape.width + padding &&
    ly >= top - padding &&
    ly <= top + shape.height + padding
  );
}
export function selectionVisible(renderer, selected) {
  if (!selected) return false;
  if (selected.kind === "switches" && junctionLod(renderer).opacity < 0.2)
    return false;
  if (renderer.focusRouteId)
    return selected.kind === "turntables"
      ? !!renderer.layers.turntables
      : selected.kind === "routes" && selected.id === renderer.focusRouteId;
  if (selected.kind === "locations") {
    const location = allLocations(renderer.store).find(
      (s) => s.id === selected.id,
    );
    return !!location && locationVisible(location, renderer.layers);
  }
  const layer = {
    cars: "trains",
    trains: "trains",
    wagonGroups: "trains",
    turntables: "turntables",
    switches: "switches",
    signals: "signals",
    signs: "signs",
    players: "players",
    tracks: "tracks",
    blocks: "blocks",
    routes: "routes",
  }[selected.kind];
  if (selected.kind === "signals") {
    const signal = renderer.store.signals.get(selected.id);
    return (
      !!signal &&
      signalVisible(signal, renderer.layers) &&
      signalLod(renderer, signal).opacity *
        (signalLayout(renderer, signal).crowdingOpacity ?? 1) >=
        0.2
    );
  }
  if (selected.kind === "blocks")
    return !!(
      renderer.layers.tracks ||
      renderer.layers.blocks ||
      renderer.layers.reservations
    );
  if (selected.kind === "signs")
    return (
      !!(renderer.layers.signs || renderer.layers.additionalSigns) &&
      aboveLod(renderer, "lodSigns", 0.15) &&
      !!railwaySigns(renderer.store).find(
        (s) => s.id === selected.id && signVisible(s, renderer.layers),
      )
    );
  return !!renderer.layers[layer];
}
export function outline(ctx, shape, padding = 3) {
  ctx.translate(shape.offsetX || 0, shape.offsetY || 0);
  ctx.rotate(shape.angle || 0);
  const x = (shape.left ?? -shape.width / 2) - padding,
    y = (shape.top ?? -shape.height / 2) - padding;
  ctx.beginPath();
  if (shape.circleRadius) {
    ctx.arc(0, 0, shape.circleRadius + padding, 0, Math.PI * 2);
    ctx.stroke();
    return;
  }
  if (shape.diamond) {
    const radius = shape.width / 2 + padding;
    ctx.moveTo(0, -radius);
    ctx.lineTo(radius, 0);
    ctx.lineTo(0, radius);
    ctx.lineTo(-radius, 0);
    ctx.closePath();
    ctx.stroke();
    return;
  }
  if (ctx.roundRect)
    ctx.roundRect(
      x,
      y,
      shape.width + padding * 2,
      shape.height + padding * 2,
      3,
    );
  else ctx.rect(x, y, shape.width + padding * 2, shape.height + padding * 2);
  ctx.stroke();
}
