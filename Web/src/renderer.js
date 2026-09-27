import { aboveLod } from "./lod.js";
import { SignalTooltip } from "./signal-tooltip.js";
import { objectFocus } from "./object-focus.js";
import { PlacementQueue } from "./placement-queue.js";
import { beginFocus, advanceFocus, clampZoom } from "./camera.js";
import { mapPalette, offsetPolyline } from "./map-palette.js";
import { bindMapInput } from "./map-input.js";
import { signalLod } from "./signal-lod.js";
import { signalFootprint } from "./signal-footprint.js";
import { mapCars, carPath } from "./rolling-stock.js";
import { railwaySigns, signVisible } from "./railway-objects.js";
import { signLayout } from "./sign-geometry.js";
import { entityName, trackName } from "./display-names.js";
import { readSetting, saveSetting } from "./storage.js";
import { allLocations, locationVisible } from "./locations.js";
import { drawSignalHead, drawSignalConnector } from "./signal-geometry.js";
import { carDisplayColor, normalizeColorMode } from "./job-display.js";
import { readLayers } from "./layers.js";
import { drawTurntables } from "./turntables.js";
import { drawRoute, routeBounds, routeHit } from "./route-display.js";
import {
  signalLayout,
  advanceSignalLayouts,
  carLayout,
  rectHit,
  selectionVisible,
  outline,
} from "./selection-display.js";
import { signalVisible } from "./signal-display.js";
import {
  junctionGeometry,
  drawJunction,
  junctionPadding,
  junctionBadge,
} from "./junction-display.js";
import { preferences, mapMetrics } from "./preferences.js";
import { drawDetails } from "./map-details.js";
import { number, t } from "./localization.js";

class SpatialIndex {
  constructor(cell = 250) {
    this.cell = cell;
    this.bins = new Map();
    this.all = [];
  }
  add(item) {
    this.all.push(item);
    const { box: b } = item;
    for (
      let x = Math.floor(b[0] / this.cell);
      x <= Math.floor(b[2] / this.cell);
      x++
    )
      for (
        let z = Math.floor(b[1] / this.cell);
        z <= Math.floor(b[3] / this.cell);
        z++
      ) {
        const key = x + "," + z;
        if (!this.bins.has(key)) this.bins.set(key, []);
        this.bins.get(key).push(item);
      }
  }
  query(b) {
    const x0 = Math.floor(b[0] / this.cell),
      x1 = Math.floor(b[2] / this.cell),
      z0 = Math.floor(b[1] / this.cell),
      z1 = Math.floor(b[3] / this.cell);
    const test = (i) =>
      i.box[0] <= b[2] &&
      i.box[2] >= b[0] &&
      i.box[1] <= b[3] &&
      i.box[3] >= b[1];
    if ((x1 - x0 + 1) * (z1 - z0 + 1) > 2500) return this.all.filter(test);
    const items = new Set();
    for (let x = x0; x <= x1; x++)
      for (let z = z0; z <= z1; z++)
        for (const i of this.bins.get(x + "," + z) || [])
          if (test(i)) items.add(i);
    return [...items];
  }
}

export class Renderer extends EventTarget {
  constructor(store) {
    super();
    this.layoutOwner = this;
    this.placementQueue = new PlacementQueue(
      () => {
        this.infrastructureDirty =
          this.interactionDirty =
          this.hoverDirty =
            true;
      },
      (region) => this.signalPlacementPlan?.regions.get(region.id) === region,
    );
    this.store = store;
    this.root = document.getElementById("map");
    this.canvases = [
      "tracks",
      "state",
      "motion",
      "interaction",
      "infrastructure",
    ].map((id) => document.getElementById(id));
    this.contexts = this.canvases.map((x) =>
      x.getContext("2d", { alpha: true }),
    );
    this.width = 1;
    this.height = 1;
    this.scale = 0.05;
    this.cx = 0;
    this.cz = 0;
    this.layers = readLayers();
    this.junctionGeometry = new Map();
    this.paths = new Map();
    this.index = new SpatialIndex();
    this.visible = [];
    this.selected = null;
    this.focusRouteId = null;
    this.follow = null;
    this.staticDirty = true;
    this.overlayDirty = true;
    this.overlayRects = [];
    this.interactionDirty = true;
    this.frames = 0;
    this.fps = 0;
    this.renderMs = 0;
    this.lastFps = performance.now();
    this.lastFrame = 0;
    const savedColor = readSetting("ads.colors");
    this.colorMode = normalizeColorMode(savedColor);
    if (savedColor !== this.colorMode)
      saveSetting("ads.colors", this.colorMode);
    this.tooltip = new SignalTooltip(this);
    this.input();
    this.resizeObserver = new ResizeObserver(() => this.resize());
    this.resizeObserver.observe(this.root);
    this.onChange = (e) => this.changed(e.detail);
    store.addEventListener("change", this.onChange);
    this.onDisplay = (event) => {
      if (event.detail?.key === "signalTooltip") {
        this.tooltip.hide();
        this.hoverDirty = true;
        return;
      }
      this.resize();
      this.invalidate();
    };
    this.onLanguage = () => this.invalidate();
    // A page in the browser's back/forward cache can resume this same renderer.
    this.onPageHide = (event) => {
      this.saveCamera();
      if (!event.persisted) this.dispose();
    };
    window.addEventListener("ads-display", this.onDisplay);
    window.addEventListener("ads-language", this.onLanguage);
    window.addEventListener("pagehide", this.onPageHide);
    this.animate = this.animate.bind(this);
    this.animationFrame = requestAnimationFrame(this.animate);
  }
  dispose() {
    if (this.disposed) return;
    this.disposed = true;
    globalThis.cancelAnimationFrame?.(this.animationFrame);
    this.resizeObserver.disconnect?.();
    this.unbindInput?.();
    this.tooltip.dispose();
    this.store.removeEventListener("change", this.onChange);
    window.removeEventListener("ads-display", this.onDisplay);
    window.removeEventListener("ads-language", this.onLanguage);
    window.removeEventListener("pagehide", this.onPageHide);
    this.signalWork?.return();
    this.signalWork = null;
    for (const work of this.signalInputWork?.values() || []) work.return();
    this.signalInputWork?.clear();
    this.placementQueue.dispose();
    this.signalPlacementPlan = null;
    this.initialSignalGroups = null;
  }
  saveCamera() {
    if (!this.paths.size || !this.store.topology?.epoch) return;
    saveSetting(
      "ads.camera",
      JSON.stringify({
        epoch: this.store.topology.epoch,
        cx: this.cx,
        cz: this.cz,
        scale: this.scale,
      }),
    );
  }
  restoreCamera() {
    try {
      const saved = JSON.parse(readSetting("ads.camera") || "null");
      if (
        saved?.epoch !== this.store.topology?.epoch ||
        ![saved.cx, saved.cz, saved.scale].every(Number.isFinite) ||
        saved.scale <= 0
      )
        return;
      this.cx = saved.cx;
      this.cz = saved.cz;
      this.scale = clampZoom(saved.scale);
      this.invalidate();
    } catch {
      /* Invalid or unavailable storage leaves the fitted native network. */
    }
  }
  theme() {
    const s = getComputedStyle(document.body);
    this.colors = {
      track: s.getPropertyValue("--track").trim(),
      red: s.getPropertyValue("--red").trim(),
      yellow: s.getPropertyValue("--yellow").trim(),
      green: s.getPropertyValue("--green").trim(),
      ink: s.getPropertyValue("--ink").trim(),
      muted: s.getPropertyValue("--muted").trim(),
      accent: s.getPropertyValue("--accent").trim(),
      map: s.getPropertyValue("--map").trim(),
    };
    this.invalidate();
  }
  resize() {
    const width = this.root.clientWidth,
      height = this.root.clientHeight,
      dpr = Math.min(2, devicePixelRatio || 1);
    if (width <= 0 || height <= 0) return;
    if (width === this.width && height === this.height && dpr === this.dpr)
      return;
    this.width = width;
    this.height = height;
    this.dpr = dpr;
    if (this.pointerClient) {
      const rect = this.root.getBoundingClientRect(),
        [x, y] = this.pointerClient;
      this.pointer = [x - rect.left, y - rect.top];
      if (
        this.pointer[0] < 0 ||
        this.pointer[1] < 0 ||
        this.pointer[0] > width ||
        this.pointer[1] > height
      ) {
        this.pointer = null;
        this.tooltip?.hide();
      }
    }
    for (const c of this.canvases) {
      const pixelsWide = Math.round(width * dpr),
        pixelsHigh = Math.round(height * dpr);
      if (c.width !== pixelsWide) c.width = pixelsWide;
      if (c.height !== pixelsHigh) c.height = pixelsHigh;
      if (c.style.width !== width + "px") c.style.width = width + "px";
      if (c.style.height !== height + "px") c.style.height = height + "px";
    }
    if (!this.colors) this.theme();
    if (this.focusRouteId) this.fitRoute();
    this.invalidate();
    // Resizing the backing buffer clears it immediately. Repaint before the
    // browser presents that frame, even when the regular animation is throttled.
    this.drawTracks();
    this.drawOverlay();
    this.drawMotion();
    this.drawInteraction();
    this.staticDirty = this.overlayDirty = this.interactionDirty = false;
    this.overlayRects = [];
  }
  project(x, z) {
    return [
      (x - this.cx) * this.scale + this.width / 2,
      (this.cz - z) * this.scale + this.height / 2,
    ];
  }
  world(x, y) {
    return [
      (x - this.width / 2) / this.scale + this.cx,
      this.cz - (y - this.height / 2) / this.scale,
    ];
  }
  view(pad = 30) {
    const a = this.world(-pad, this.height + pad),
      b = this.world(this.width + pad, -pad);
    return [a[0], a[1], b[0], b[1]];
  }
  invalidate() {
    this.infrastructureDirty = true;
    this.staticDirty = true;
    this.overlayDirty = true;
    this.interactionDirty = true;
    this.hoverDirty = true;
    this.visible = this.index.query(this.view());
  }
  changed({ kind, payload }) {
    const caps = this.store.capabilities;
    const capabilityVisual = [
      caps.status,
      caps.mode,
      caps.signals,
      caps.language,
    ].join(":");
    const capabilityChanged = this.capabilityVisual !== capabilityVisual;
    this.capabilityVisual = capabilityVisual;
    if (
      ["snapshot", "topology"].includes(kind) ||
      (payload?.capabilities && capabilityChanged) ||
      payload?.blocks?.length ||
      payload?.removedBlocks?.length ||
      payload?.reset
    ) {
      if (["snapshot", "topology"].includes(kind) || payload?.reset) {
        this.signalWork?.return();
        this.signalWork = null;
        for (const work of this.signalInputWork?.values() || []) work.return();
        this.signalInputWork?.clear();
        this.signalPlacementPlan = null;
        this.initialSignalGroups = null;
        this.signalEntryCache?.clear();
        this.signalLanes?.clear();
        this.signalLayouts = null;
      }
      this.overlayDirty = true;
    }
    if (payload?.signals?.length || payload?.removedSignals?.length) {
      this.infrastructureChanges ||= new Set();
      for (const s of payload.signals || [])
        this.infrastructureChanges.add(s.id);
      for (const id of payload.removedSignals || [])
        this.infrastructureChanges.add(id);
    }
    if (
      payload?.switches?.length ||
      payload?.signs?.length ||
      payload?.replaceSigns ||
      payload?.turntables?.length
    )
      this.infrastructureDirty = true;
    for (const id of payload?.removedSignals || []) {
      this.signalLanes?.delete(id);
      this.signalEntryCache?.delete(id);
      this.signalLayouts?.delete(id);
      this.signalOverviewLayouts?.delete(id);
    }
    if (payload?.locations || payload?.replaceLocations)
      this.overlayDirty = true;
    this.hoverDirty = true;
    if (
      this.focusRouteId &&
      ["snapshot", "topology", "routes"].includes(kind)
    ) {
      const route = this.store.routes.find((r) => r.id === this.focusRouteId);
      if (!route || route.endedAt) this.clearRouteFocus();
      else this.updateRouteFocusBanner(route);
    }
    if (this.selected && !this.resolve(this.selected)) this.select(null);
    if (kind === "snapshot" || kind === "topology") {
      this.occupancyVisual = new Map(
        [...this.store.occupancy].map(([id, o]) => [id, o.occupied]),
      );
      this.tableVisual = new Map();
      if (
        this.lastTopologyEpoch &&
        this.lastTopologyEpoch !== this.store.topology?.epoch
      ) {
        this.follow = null;
        this.select(null);
      }
      this.rebuildBlockVisual();
      this.speedPaths = null;
      this.build();
      this.invalidate();
      return;
    }
    if (kind === "capabilities") {
      this.overlayDirty = true;
      this.interactionDirty = true;
      return;
    }
    if (kind === "routes") {
      this.overlayDirty = true;
      this.interactionDirty = true;
      return;
    }
    if (kind !== "delta") return;
    if (payload.signs?.length || payload.replaceSigns || payload.reset) {
      this.speedPaths = null;
      this.overlayDirty = true;
    } else if (payload.switches?.length) {
      this.speedPaths = null;
      if (
        this.layers.speedLimits ||
        this.layers.speedRestrictions ||
        this.layers.signs
      )
        this.overlayDirty = true;
    }
    if (payload.blocks?.length || payload.removedBlocks?.length)
      this.rebuildBlockVisual();
    if (payload.reset) {
      this.rebuildBlockVisual();
      this.overlayDirty = true;
      this.interactionDirty = true;
      return;
    }
    for (const value of payload.turntables || []) {
      this.tableVisual ||= new Map();
      const key = [
        value.angle,
        value.target,
        value.moving,
        value.available,
        value.reason,
        value.front,
        value.rear,
        ...(value.points || []),
      ].join(":");
      if (this.tableVisual.get(value.id) === key) continue;
      this.tableVisual.set(value.id, key);
      this.speedPaths = null;
      const def = this.store.tableDefs.get(value.id);
      if (def) this.dirtyPoint(def.x, def.z, def.radius * this.scale + 30);
    }
    for (const sw of payload.switches || []) {
      const p = this.store.junctions.get(sw.id);
      if (p) this.dirtyPoint(p.x, p.z, junctionPadding);
      if (p && this.layers.switchBranches)
        for (const id of p.branches || []) this.dirtyTrack(id);
    }
    this.occupancyVisual ||= new Map();
    for (const o of payload.occupancy || []) {
      if (this.occupancyVisual.get(o.id) === o.occupied) continue;
      this.occupancyVisual.set(o.id, o.occupied);
      this.dirtyTrack(o.id);
    }
    for (const b of payload.blocks || [])
      for (const id of [...b.tracks, ...b.extraTracks]) this.dirtyTrack(id);
    if (this.overlayRects.length > 40) {
      this.overlayDirty = true;
      this.overlayRects = [];
    }
    this.interactionDirty = true;
  }
  dirtyPoint(x, z, padding) {
    const p = this.project(x, z);
    this.overlayRects.push([
      p[0] - padding,
      p[1] - padding,
      padding * 2,
      padding * 2,
    ]);
  }
  dirtyTrack(id) {
    const path = this.paths.get(id);
    if (!path) return;
    const a = this.project(path.box[0], path.box[3]),
      b = this.project(path.box[2], path.box[1]);
    this.overlayRects.push([
      a[0] - 8,
      a[1] - 8,
      b[0] - a[0] + 16,
      b[1] - a[1] + 16,
    ]);
  }
  rebuildBlockVisual() {
    this.blockVisual = new Map();
    for (const b of this.store.blocks.values())
      if (b.occupied || b.reserved)
        for (const id of [...b.tracks, ...b.extraTracks]) {
          const v = this.blockVisual.get(id) || {
            occupied: false,
            reserved: false,
          };
          v.occupied ||= b.occupied;
          v.reserved ||= b.reserved;
          this.blockVisual.set(id, v);
        }
  }
  build() {
    this.paths = new Map();
    this.junctionGeometry = new Map(
      [...this.store.junctions.values()].map((j) => [
        j.id,
        junctionGeometry(j, this.store.tracks),
      ]),
    );
    this.index = new SpatialIndex();
    for (const track of this.store.tracks.values()) {
      const path = new Path2D();
      const box = [Infinity, Infinity, -Infinity, -Infinity];
      for (let i = 0; i < track.points.length; i += 2) {
        const x = track.points[i],
          z = track.points[i + 1];
        if (i === 0) path.moveTo(x, -z);
        else path.lineTo(x, -z);
        box[0] = Math.min(box[0], x);
        box[1] = Math.min(box[1], z);
        box[2] = Math.max(box[2], x);
        box[3] = Math.max(box[3], z);
      }
      let label = null;
      for (let i = 2; i < track.points.length; i += 2) {
        const dx = track.points[i] - track.points[i - 2],
          dz = track.points[i + 1] - track.points[i - 1],
          length = Math.hypot(dx, dz),
          score = length / (1 + Math.abs(dz) / (length || 1));
        if (!label || score > label.score)
          label = {
            x: (track.points[i] + track.points[i - 2]) / 2,
            z: (track.points[i + 1] + track.points[i - 1]) / 2,
            length,
            score,
          };
      }
      const item = { id: track.id, path, box, label };
      this.paths.set(track.id, item);
      this.index.add(item);
    }
    if (this.lastTopologyEpoch !== this.store.topology?.epoch) {
      const first = this.lastTopologyEpoch == null;
      this.lastTopologyEpoch = this.store.topology?.epoch;
      this.fit();
      if (first) this.restoreCamera();
    } else this.invalidate();
    document.getElementById("empty-map").hidden = this.paths.size > 0;
  }
  fit() {
    this.cameraFocus = null;
    if (!this.paths.size) return;
    let box = [Infinity, Infinity, -Infinity, -Infinity];
    for (const p of this.paths.values()) {
      box[0] = Math.min(box[0], p.box[0]);
      box[1] = Math.min(box[1], p.box[1]);
      box[2] = Math.max(box[2], p.box[2]);
      box[3] = Math.max(box[3], p.box[3]);
    }
    this.cx = (box[0] + box[2]) / 2;
    this.cz = (box[1] + box[3]) / 2;
    this.scale = Math.min(
      (this.width - 100) / Math.max(1, box[2] - box[0]),
      (this.height - 100) / Math.max(1, box[3] - box[1]),
    );
    this.follow = null;
    this.invalidate();
  }
  center(x, z, zoom) {
    this.cameraFocus = null;
    this.follow = null;
    this.cx = x;
    this.cz = z;
    if (zoom) this.scale = clampZoom(Math.max(this.scale, zoom));
    this.invalidate();
  }
  zoom(factor, x = this.width / 2, y = this.height / 2) {
    this.follow = null;
    // Selection starts an explicit focus transition. Wheel zoom owns its own
    // cursor anchor and must never complete that transition by teleporting.
    this.cameraFocus = null;
    const before = this.world(x, y);
    this.scale = clampZoom(this.scale * factor);
    const after = this.world(x, y);
    this.cx += before[0] - after[0];
    this.cz += before[1] - after[1];
    this.invalidate();
  }
  updateRouteFocusBanner(route) {
    const banner = document.getElementById("route-focus"),
      name = document.getElementById("route-focus-name");
    if (banner) banner.hidden = !route;
    if (name)
      name.textContent = route
        ? t("routeFocusActive") +
          " · " +
          entityName(this.store, "routes", route)
        : "";
  }
  toggleRouteFocus(route) {
    if (this.focusRouteId === route.id) {
      this.clearRouteFocus();
      return;
    }
    if (!this.focusRouteId)
      this.beforeRouteFocus = {
        cx: this.cx,
        cz: this.cz,
        scale: this.scale,
      };
    this.focusRouteId = route.id;
    document.body.classList?.add?.("route-overview");
    this.follow = null;
    this.select({ kind: "routes", id: route.id });
    this.updateRouteFocusBanner(route);
    this.fitRoute(route);
    this.invalidate();
    this.dispatchEvent(new Event("route-focus"));
  }
  clearRouteFocus() {
    this.focusRouteId = null;
    document.body.classList?.remove?.("route-overview");
    if (this.beforeRouteFocus) Object.assign(this, this.beforeRouteFocus);
    this.beforeRouteFocus = null;
    this.updateRouteFocusBanner(null);
    this.invalidate();
    this.dispatchEvent(new Event("route-focus"));
  }
  fitRoute(route = this.store.routes.find((r) => r.id === this.focusRouteId)) {
    this.cameraFocus = null;
    const box = routeBounds(route, this.store);
    if (!box) return;
    this.cx = (box[0] + box[2]) / 2;
    this.cz = (box[1] + box[3]) / 2;
    this.scale = Math.max(
      0.001,
      Math.min(
        8,
        Math.max(20, this.width - 80) / Math.max(1, box[2] - box[0]),
        Math.max(20, this.height - 80) / Math.max(1, box[3] - box[1]),
      ),
    );
    this.invalidate();
  }
  focusPosition(item, zoom = this.scale) {
    return objectFocus(this, item, zoom);
  }
  focusSelection(item = this.selected) {
    if (item) beginFocus(this, item);
  }
  select(item) {
    if (
      this.focusRouteId &&
      (!item || item.kind !== "routes" || item.id !== this.focusRouteId)
    ) {
      const view = item
        ? { cx: this.cx, cz: this.cz, scale: this.scale }
        : null;
      this.clearRouteFocus();
      if (view) Object.assign(this, view);
    }
    if (this.selected?.kind === "signals" || item?.kind === "signals")
      this.overlayDirty = true;
    this.cameraFocus = null;
    this.follow = null;
    this.selected = item;
    this.focusSelection(item);
    this.interactionDirty = true;
    this.dispatchEvent(new CustomEvent("select", { detail: item }));
  }
  resolve(item, store = this.store) {
    if (!item) return null;
    if (item.kind === "wagonGroups") return store.wagonGroup(item.id);
    if (item.kind === "routes")
      return store.routes.find((r) => r.id === item.id);
    if (item.kind === "log") return store.events.find((e) => e.id === item.id);
    if (item.kind === "locations")
      return allLocations(store).find((s) => s.id === item.id);
    if (
      item.kind === "signals" &&
      store.signals.get(item.id)?.objectKind === "sign"
    )
      return null;
    if (item.kind === "signs")
      return railwaySigns(store).find((s) => s.id === item.id);
    if (item.kind === "trains") return store.train(item.id);
    if (
      item.kind === "blocks" &&
      !store.blocks.size &&
      store.tracks.has(item.id)
    ) {
      const track = store.tracks.get(item.id),
        occupancy = store.occupancy.get(item.id);
      return {
        id: track.id,
        name: track.name,
        tracks: [track.id],
        length: track.length,
        occupied: occupancy?.occupied,
        quality: occupancy ? "ready" : "unknown",
        source: "dispatch",
      };
    }
    if (item.kind === "switches")
      return store.junctions.has(item.id)
        ? {
            ...store.junctions.get(item.id),
            ...store.switches.get(item.id),
          }
        : null;
    if (item.kind === "turntables")
      return store.tableDefs.has(item.id)
        ? {
            ...store.tableDefs.get(item.id),
            ...store.turntables.get(item.id),
          }
        : null;
    if (item.kind === "tracks") {
      const track = store.tracks.get(item.id);
      if (!track) return null;
      const middle = Math.floor(track.points.length / 4) * 2;
      return { ...track, x: track.points[middle], z: track.points[middle + 1] };
    }
    return store[item.kind]?.get?.(item.id);
  }
  input() {
    this.unbindInput?.();
    this.unbindInput = bindMapInput(this);
  }
  hit(x, y, options = {}) {
    if (options.tracksOnly || (this.trackPicking && !options.hover))
      return this.hitTrack(x, y);
    if (this.focusRouteId) {
      if (this.layers.turntables)
        for (const table of this.store.tableDefs.values()) {
          const p = this.project(table.x, table.z);
          if (
            Math.hypot(x - p[0], y - p[1]) <=
            Math.max(9, table.radius * this.scale) + 3
          )
            return { kind: "turntables", id: table.id };
        }
      const route = this.store.routes.find((r) => r.id === this.focusRouteId);
      return routeHit(route, this.store, (x, z) => this.project(x, z), x, y)
        ? { kind: "routes", id: this.focusRouteId }
        : null;
    }
    let selected = null,
      best = Infinity,
      bestPriority = -1;
    const check = (item, kind, shape) => {
      const p = this.project(item.x, item.z),
        dx = x - p[0] - (shape.offsetX || 0),
        dy = y - p[1] - (shape.offsetY || 0);
      const inside = shape.circleRadius
        ? Math.hypot(dx, dy) <= shape.circleRadius + 3
        : rectHit(dx, dy, shape);
      const distance = Math.hypot(dx, dy);
      const priority =
        {
          cars: 1,
          players: 2,
          locations: 3,
          signs: 4,
          switches: 5,
          signals: 6,
          turntables: 0,
        }[kind] ?? 0;
      if (
        inside &&
        (priority > bestPriority ||
          (priority === bestPriority && distance < best))
      ) {
        bestPriority = priority;
        best = distance;
        selected = { kind, id: item.id };
      }
    };
    if (!this.focusRouteId) {
      if (this.layers.trains) {
        for (const car of mapCars(this)) {
          const pose = { ...car, ...this.store.position(car, this.frameTime) };
          check(pose, "cars", carLayout(this, pose));
        }
      }
      if (this.layers.players)
        for (const player of this.store.players.values()) {
          const p = {
            ...player,
            ...this.store.playerPosition(player, this.frameTime),
          };
          check(p, "players", {
            width: 10,
            height: 14,
            angle: (p.yaw * Math.PI) / 180,
          });
        }
      if (
        (this.layers.signals || this.layers.shuntingSignals) &&
        signalLod(this).visible
      )
        for (const signal of this.store.signals.values())
          if (signalVisible(signal, this.layers)) {
            const p = this.project(signal.x, signal.z);
            const reach =
              (512 *
                Math.max(preferences.signalScale, preferences.indicatorScale)) /
              100;
            if (Math.abs(p[0] - x) > reach || Math.abs(p[1] - y) > reach)
              continue;
            const shape = signalLayout(this, signal);
            if (
              signalLod(this, signal).opacity * (shape.crowdingOpacity ?? 1) >=
              0.2
            )
              check(signal, "signals", shape);
          }
      if (this.layers.switches)
        for (const j of this.store.junctions.values()) {
          const badge = junctionBadge(this, j);
          if (badge.lod.opacity >= 0.2) check(j, "switches", badge);
        }
      if (this.layers.turntables)
        for (const table of this.store.tableDefs.values())
          check(table, "turntables", {
            circleRadius: Math.max(9, table.radius * this.scale),
          });
      for (const location of allLocations(this.store))
        if (locationVisible(location, this.layers))
          check(location, "locations", { width: 12, height: 12 });
      if (
        (this.layers.signs || this.layers.additionalSigns) &&
        aboveLod(this, "lodSigns", 0.15)
      )
        for (const sign of railwaySigns(this.store))
          if (signVisible(sign, this.layers))
            check(sign, "signs", signLayout(this, sign));
      if (selected) return selected;
    }
    return this.hitTrack(x, y);
  }
  hitTrack(x, y) {
    if (
      !this.focusRouteId &&
      !this.layers.tracks &&
      !this.layers.routes &&
      !this.layers.blocks &&
      !this.layers.reservations
    )
      return null;
    const w = this.world(x, y),
      radius = 8 / this.scale;
    let best = 8,
      selected = null;
    for (const item of this.index.query([
      w[0] - radius,
      w[1] - radius,
      w[0] + radius,
      w[1] + radius,
    ])) {
      if (this.store.tableTracks.has(item.id)) continue;
      const p = this.store.tracks.get(item.id).points;
      for (let i = 2; i < p.length; i += 2) {
        const a = this.project(p[i - 2], p[i - 1]),
          b = this.project(p[i], p[i + 1]);
        const dx = b[0] - a[0],
          dy = b[1] - a[1],
          u = Math.max(
            0,
            Math.min(
              1,
              ((x - a[0]) * dx + (y - a[1]) * dy) / (dx * dx + dy * dy || 1),
            ),
          );
        const d = Math.hypot(x - a[0] - u * dx, y - a[1] - u * dy);
        if (d < best) {
          best = d;
          selected = { kind: "tracks", id: item.id };
        }
      }
    }
    if (!selected) return null;
    return selected;
  }
  setup(ctx) {
    ctx.setTransform(this.dpr, 0, 0, this.dpr, 0, 0);
  }
  worldTransform(ctx) {
    ctx.translate(
      this.width / 2 - this.cx * this.scale,
      this.height / 2 + this.cz * this.scale,
    );
    ctx.scale(this.scale, this.scale);
  }
  drawTracks() {
    this.trackLabelBoxes = [];
    const ctx = this.contexts[0];
    this.setup(ctx);
    ctx.clearRect(0, 0, this.width, this.height);
    if (!this.layers.tracks) return;
    ctx.save();
    this.worldTransform(ctx);
    ctx.strokeStyle = this.colors.track;
    ctx.lineWidth = (1.6 * preferences.trackScale) / 100 / this.scale;
    ctx.lineCap = "round";
    for (const item of this.visible)
      if (!this.store.tableTracks.has(item.id)) ctx.stroke(item.path);
    ctx.restore();
    if (
      !this.focusRouteId &&
      this.layers.labels &&
      aboveLod(this, "lodTrackLabels", 0.6)
    ) {
      ctx.font = `${(mapMetrics.label * preferences.labelScale) / 100}px Segoe UI`;
      ctx.fillStyle = this.colors.muted;
      ctx.textAlign = "center";
      for (const item of this.visible) {
        const track = this.store.tracks.get(item.id);
        if (
          track.name.includes("#") ||
          this.store.tableTracks.has(item.id) ||
          /^\[track (diverging|through)\]$/i.test(track.name)
        )
          continue;
        const anchor = item.label;
        if (!anchor) continue;
        const p = this.project(anchor.x, anchor.z);
        const text = trackName(this.store, track);
        const width = ctx.measureText(text).width;
        const box = [
          p[0] - width / 2 - 3,
          p[1] - 8 - (mapMetrics.label * preferences.labelScale) / 100,
          p[0] + width / 2 + 3,
          p[1] - 5,
        ];
        if (
          this.trackLabelBoxes.some(
            (b) =>
              box[0] < b[2] && box[2] > b[0] && box[1] < b[3] && box[3] > b[1],
          )
        )
          continue;
        this.trackLabelBoxes.push(box);
        ctx.strokeStyle = this.colors.map;
        ctx.lineWidth = 3;
        ctx.strokeText(text, p[0], p[1] - 8);
        ctx.fillText(text, p[0], p[1] - 8);
      }
    }
  }
  strokeTrack(ctx, id, color, width = 3, scale = preferences.trackScale) {
    const item = this.paths.get(id);
    if (!item) return;
    const b = item.box,
      pad = ((width * scale) / 100 + 2) / this.scale;
    if (
      b &&
      (b[2] < this.cx - this.width / 2 / this.scale - pad ||
        b[0] > this.cx + this.width / 2 / this.scale + pad ||
        b[3] < this.cz - this.height / 2 / this.scale - pad ||
        b[1] > this.cz + this.height / 2 / this.scale + pad)
    )
      return;
    ctx.save();
    this.worldTransform(ctx);
    ctx.strokeStyle = color;
    ctx.lineWidth = (width * scale) / 100 / this.scale;
    if (this.store.tableTracks.has(id)) {
      const points = this.store.tracks.get(id).points;
      ctx.beginPath();
      for (let i = 0; i < points.length; i += 2) {
        if (i === 0) ctx.moveTo(points[i], -points[i + 1]);
        else ctx.lineTo(points[i], -points[i + 1]);
      }
      ctx.stroke();
    } else ctx.stroke(item.path);
    ctx.restore();
  }
  drawOverlay(rect) {
    const ctx = this.contexts[1];
    this.setup(ctx);
    ctx.save();
    if (rect) {
      ctx.beginPath();
      ctx.rect(...rect);
      ctx.clip();
      ctx.clearRect(...rect);
    } else ctx.clearRect(0, 0, this.width, this.height);
    if (this.focusRouteId) {
      const route = this.store.routes.find((r) => r.id === this.focusRouteId);
      drawTurntables(this, ctx);
      if (route) drawRoute(this, ctx, route, true);
      this.drawInfrastructure();
      ctx.restore();
      return;
    }
    drawDetails(this, ctx, "background");
    if (this.layers.blocks || this.layers.reservations) {
      if (!this.blockVisual) this.rebuildBlockVisual();
      for (const [id, b] of this.blockVisual) {
        const occupied = b.occupied && this.layers.blocks,
          reserved = b.reserved && this.layers.reservations;
        if (!occupied && !reserved) continue;
        if (reserved) {
          ctx.setLineDash([4, 4]);
          this.strokeTrack(ctx, id, mapPalette.reservation, 7);
        }
        if (occupied) {
          ctx.setLineDash([]);
          this.strokeTrack(
            ctx,
            id,
            this.colors.red,
            1.6,
            preferences.occupancyScale,
          );
        }
      }
      ctx.setLineDash([]);
      for (const o of this.store.occupancy.values())
        if (this.layers.blocks && o.occupied)
          this.strokeTrack(
            ctx,
            o.id,
            this.colors.red,
            1.6,
            preferences.occupancyScale,
          );
    }
    if (this.layers.reservations) {
      ctx.setLineDash([6, 3]);
      for (const route of this.store.routes)
        if (!route.endedAt && route.reservationState === "reserved")
          for (const id of route.tracks)
            this.strokeTrack(
              ctx,
              id,
              route.reservationMode === "protected"
                ? mapPalette.protectedReservation
                : mapPalette.reservation,
              7,
            );
      ctx.setLineDash([]);
    }
    // Occupancy is repainted as a solid centreline inside reservation edges.
    if (this.layers.blocks)
      for (const o of this.store.occupancy.values())
        if (o.occupied)
          this.strokeTrack(
            ctx,
            o.id,
            this.colors.red,
            1.6,
            preferences.occupancyScale,
          );
    if (this.layers.routes) {
      ctx.setLineDash([5, 4]);
      for (const route of [
        ...this.store.routes.filter((r) => !r.endedAt),
        ...(this.store.preview ? [this.store.preview] : []),
      ])
        for (const id of route.tracks)
          this.strokeTrack(ctx, id, mapPalette.route, 2.2);
      ctx.setLineDash([]);
    }
    if (this.layers.switches || this.layers.switchBranches)
      for (const j of this.store.junctions.values())
        drawJunction(this, ctx, j, "background");
    drawTurntables(this, ctx);
    ctx.restore();
    if (this.infrastructureDirty || !this.signalPaintBounds)
      this.drawInfrastructure();
  }
  drawInfrastructure() {
    const partial =
      !this.infrastructureDirty &&
      this.signalPaintBounds &&
      this.infrastructureChanges?.size;
    const overlaps = (a, b) =>
      a[0] < b[2] && a[2] > b[0] && a[1] < b[3] && a[3] > b[1];
    const signalBox = (s, shape) => {
      const [x, y] = this.project(s.x, s.z),
        f = signalFootprint(shape);
      const sx = x + (shape.offsetX || 0) + f.cx,
        sy = y + (shape.offsetY || 0) + f.cy;
      const ax = x + (shape.anchorX || 0),
        ay = y + (shape.anchorY || 0);
      return [
        Math.floor(Math.min(ax, sx - f.w / 2) - 4),
        Math.floor(Math.min(ay, sy - f.h / 2) - 4),
        Math.ceil(Math.max(ax, sx + f.w / 2) + 4),
        Math.ceil(Math.max(ay, sy + f.h / 2) + 4),
      ];
    };
    const dirty = [];
    if (partial)
      for (const id of this.infrastructureChanges) {
        const old = this.signalPaintBounds.get(id);
        if (old) dirty.push(old);
        const s = this.store.signals.get(id);
        if (s && signalVisible(s, this.layers))
          dirty.push(signalBox(s, signalLayout(this, s)));
        this.signalPaintBounds.delete(id);
      }
    // Include whole neighbouring markers/connectors before clearing damage.
    // Cutting a neighbour at the clip boundary changes its antialias coverage.
    if (dirty.length) {
      const bounds = [
        Math.min(...dirty.map((b) => b[0])),
        Math.min(...dirty.map((b) => b[1])),
        Math.max(...dirty.map((b) => b[2])),
        Math.max(...dirty.map((b) => b[3])),
      ];
      let expanded;
      do {
        expanded = false;
        for (const box of this.signalPaintBounds.values())
          if (overlaps(bounds, box)) {
            const next = [
              Math.min(bounds[0], box[0]),
              Math.min(bounds[1], box[1]),
              Math.max(bounds[2], box[2]),
              Math.max(bounds[3], box[3]),
            ];
            if (next.some((v, i) => v !== bounds[i])) {
              bounds.splice(0, 4, ...next);
              expanded = true;
            }
          }
      } while (expanded);
      dirty.splice(0, dirty.length, bounds);
    }
    this.infrastructureChanges?.clear();
    if (!partial) this.signalPaintBounds = new Map();
    this.infrastructureDirty = false;
    const ctx = this.contexts[4];
    if (!ctx) return;
    this.setup(ctx);
    ctx.save();
    if (partial) {
      ctx.beginPath();
      for (const b of dirty) ctx.rect(b[0], b[1], b[2] - b[0], b[3] - b[1]);
      ctx.clip();
    }
    ctx.clearRect(0, 0, this.width, this.height);
    if (this.focusRouteId) {
      ctx.restore();
      return;
    }
    ctx.save();
    const heads = [];
    if (
      (this.layers.signals || this.layers.shuntingSignals) &&
      signalLod(this).visible
    )
      for (const s of this.store.signals.values()) {
        if (!signalVisible(s, this.layers) || !signalLod(this, s).visible)
          continue;
        if (partial) {
          const old = this.signalPaintBounds.get(s.id);
          if (old && !dirty.some((b) => overlaps(old, b))) continue;
          const [px, py] = this.project(s.x, s.z);
          if (
            !old &&
            !dirty.some((b) =>
              overlaps([px - 512, py - 512, px + 512, py + 512], b),
            )
          )
            continue;
        }
        const [x, y] = this.project(s.x, s.z);
        const margin =
          (512 *
            Math.max(preferences.signalScale, preferences.indicatorScale)) /
          100;
        if (
          x < -margin ||
          y < -margin ||
          x > this.width + margin ||
          y > this.height + margin
        )
          continue;
        const shape = signalLayout(this, s);
        const sx = x + (shape.offsetX || 0),
          sy = y + (shape.offsetY || 0);
        const bounds = signalFootprint(shape);
        if (
          sx + bounds.w / 2 + 14 < 0 ||
          sy + bounds.h / 2 + 14 < 0 ||
          sx - bounds.w / 2 - 14 > this.width ||
          sy - bounds.h / 2 - 14 > this.height
        )
          continue;
        const box = signalBox(s, shape);
        this.signalPaintBounds.set(s.id, box);
        if (!partial || dirty.some((b) => overlaps(box, b)))
          heads.push({ s, shape });
      }
    for (const { s, shape } of heads) drawSignalConnector(this, ctx, s, shape);
    drawDetails(this, ctx, "foreground");
    if (this.layers.switches)
      for (const junction of this.store.junctions.values())
        drawJunction(this, ctx, junction, "foreground");
    for (const { s, shape } of heads)
      drawSignalHead(this, ctx, s, shape, false);
    ctx.restore();
    ctx.restore();
  }
  carColor(car) {
    return carDisplayColor(this.store, car, this.colorMode);
  }
  drawMotion() {
    const ctx = this.contexts[2];
    const now = this.frameTime ?? this.store.presentationTime();
    const sample = this.motionSample || (this.motionSample = {});
    this.setup(ctx);
    ctx.clearRect(0, 0, this.width, this.height);
    if (this.focusRouteId) return;
    const arrows = new Set();
    if (this.layers.trains)
      for (const car of mapCars(this)) {
        const p = this.store.position(car, now, sample);
        const x = (p.x - this.cx) * this.scale + this.width / 2;
        const y = (this.cz - p.z) * this.scale + this.height / 2;
        const icon = preferences.trainScale / 100,
          shape = carLayout(this, {
            length: car.length,
            width: car.width,
            yaw: p.yaw,
          }),
          length = shape.width;
        const margin = Math.hypot(shape.width, shape.height) / 2 + 24;
        if (
          x < -margin ||
          y < -margin ||
          x > this.width + margin ||
          y > this.height + margin
        )
          continue;
        ctx.save();
        ctx.translate(x, y);
        ctx.rotate(shape.angle);
        ctx.fillStyle = this.carColor(car);
        carPath(ctx, shape, car.locomotive);
        ctx.fill();
        ctx.strokeStyle = car.derailed ? this.colors.red : this.colors.map;
        ctx.lineWidth = 0.7;
        ctx.stroke();
        ctx.restore();
        if (Math.abs(car.speed) > 0.18 && !arrows.has(car.consist)) {
          arrows.add(car.consist);
          ctx.save();
          ctx.translate(x, y);
          ctx.rotate(
            ((p.yaw - 90 + (car.speed < 0 ? 180 : 0)) * Math.PI) / 180,
          );
          ctx.strokeStyle = this.colors.ink;
          ctx.lineWidth = 1.7;
          const tip = Math.min(35, length / 2 + 17 * icon);
          ctx.beginPath();
          ctx.moveTo(tip - 12 * icon, 0);
          ctx.lineTo(tip, 0);
          ctx.moveTo(tip - 5 * icon, -4 * icon);
          ctx.lineTo(tip, 0);
          ctx.lineTo(tip - 5 * icon, 4 * icon);
          ctx.stroke();
          ctx.restore();
        }
        if (
          this.layers.labels &&
          car.locomotive &&
          aboveLod(this, "lodVehicleLabels", 0.4)
        ) {
          ctx.fillStyle = this.colors.ink;
          ctx.textAlign = "center";
          ctx.font = `${(mapMetrics.label * preferences.trainLabelScale) / 100}px Segoe UI`;
          ctx.fillText(
            entityName(this.store, "cars", car),
            x,
            y - (mapMetrics.label * preferences.trainLabelScale) / 100,
          );
        }
      }
    if (this.layers.players)
      for (const player of this.store.players.values()) {
        const pose = this.store.playerPosition(player, now);
        const [x, y] = this.project(pose.x, pose.z);
        if (x < 0 || y < 0 || x > this.width || y > this.height) continue;
        ctx.save();
        ctx.translate(x, y);
        ctx.rotate((pose.yaw * Math.PI) / 180);
        ctx.fillStyle = "#d8effa";
        ctx.beginPath();
        ctx.moveTo(0, -7);
        ctx.lineTo(5, 5);
        ctx.lineTo(0, 2);
        ctx.lineTo(-5, 5);
        ctx.closePath();
        ctx.fill();
        ctx.restore();
        if (this.layers.labels) {
          ctx.font = `${(mapMetrics.label * preferences.labelScale) / 100}px Segoe UI`;
          ctx.fillStyle = this.colors.ink;
          ctx.fillText(entityName(this.store, "players", player), x + 9, y + 3);
        }
      }
  }
  drawInteraction() {
    const ctx = this.contexts[3];
    this.setup(ctx);
    ctx.clearRect(0, 0, this.width, this.height);
    if (!selectionVisible(this, this.selected)) return;
    const object = this.resolve(this.selected, this.store);
    if (!object) return;
    const kind = this.selected.kind;
    if (kind === "routes") {
      if (!this.focusRouteId) drawRoute(this, ctx, object);
      return;
    }
    if (kind === "tracks" || kind === "blocks") {
      ctx.save();
      ctx.strokeStyle = mapPalette.selected;
      ctx.lineWidth = 1.5;
      for (const id of new Set(
        kind === "tracks"
          ? [object.id]
          : [...(object.tracks || []), ...(object.extraTracks || [])],
      )) {
        const p = this.store.tracks.get(id)?.points || [],
          points = [];
        for (let i = 0; i < p.length; i += 2)
          points.push(this.project(p[i], p[i + 1]));
        for (const side of [-1, 1]) {
          ctx.beginPath();
          offsetPolyline(
            points,
            side * (3 + preferences.trackScale / 100),
          ).forEach((v, i) => (i ? ctx.lineTo(...v) : ctx.moveTo(...v)));
          ctx.stroke();
        }
      }
      ctx.restore();
      return;
    }
    if (kind === "cars" || kind === "trains" || kind === "wagonGroups") {
      const selectedIds = new Set(
        kind === "trains" || kind === "wagonGroups"
          ? object.carIds
          : [object.id],
      );
      for (const car of this.store.cars.values()) {
        if (!selectedIds.has(car.id)) continue;
        const pose = { ...car, ...this.store.position(car, this.frameTime) };
        ctx.save();
        ctx.translate(...this.project(pose.x, pose.z));
        const shape = carLayout(this, pose);
        ctx.rotate(shape.angle);
        ctx.strokeStyle = mapPalette.selected;
        ctx.lineWidth = 2;
        carPath(ctx, shape, car.locomotive, 1.5);
        ctx.stroke();
        ctx.restore();
      }
      return;
    }
    const pose =
      kind === "players"
        ? this.store.playerPosition(object, this.frameTime)
        : object;
    if (!Number.isFinite(pose.x)) return;
    ctx.save();
    ctx.translate(...this.project(pose.x, pose.z));
    ctx.strokeStyle = mapPalette.selected;
    ctx.lineWidth = 2;
    if (kind === "signals") outline(ctx, signalLayout(this, object));
    else if (kind === "switches") outline(ctx, junctionBadge(this, object));
    else if (kind === "signs") {
      outline(ctx, signLayout(this, object));
    } else if (kind === "turntables") {
      ctx.beginPath();
      ctx.arc(
        0,
        0,
        Math.max(9, object.radius * this.scale) + 4,
        0,
        Math.PI * 2,
      );
      ctx.stroke();
    } else if (kind === "locations") outline(ctx, { width: 6, height: 6 });
    else if (kind === "players")
      outline(ctx, {
        width: 10,
        height: 14,
        angle: ((pose.yaw || 0) * Math.PI) / 180,
      });
    else
      outline(ctx, {
        width: 12,
        height: 14,
        angle: ((pose.yaw || 0) * Math.PI) / 180,
      });
    ctx.restore();
  }
  animate(now) {
    if (this.disposed) return;
    this.animationFrame = requestAnimationFrame(this.animate);
    if (
      document.hidden ||
      !this.colors ||
      now - this.lastFrame < 1000 / preferences.renderRate - 1
    )
      return;
    const start = performance.now();
    this.lastFrame = now;
    advanceSignalLayouts(this);
    this.placementMs = performance.now() - start;
    if (this.hoverDirty && this.pointer && !this.panning) {
      this.hoverDirty = false;
      const item = this.hit(...this.pointer, { hover: true });
      const method = item ? "add" : "remove";
      if (preferences.signalTooltip) this.tooltip.update(item, this.pointer);
      this.root.classList[method]("interactive");
    }
    this.frameTime = this.store.presentationTime(now);
    advanceFocus(this, now);
    if (this.follow) {
      const p = this.resolve(this.follow);
      if (p) {
        const sample = this.focusPosition(this.follow) || p;
        if (
          Math.abs(sample.x - this.cx) + Math.abs(sample.z - this.cz) >
          0.02
        ) {
          this.cx = sample.x;
          this.cz = sample.z;
          this.invalidate();
        }
      } else this.follow = null;
    }
    const stale = this.store.stale;
    if (stale !== this.wasStale) {
      this.wasStale = stale;
      this.hoverDirty = true;
      this.overlayDirty = true;
      this.interactionDirty = true;
    }
    if (this.staticDirty) {
      this.drawTracks();
      this.staticDirty = false;
    }
    if (this.overlayDirty) {
      this.drawOverlay();
      this.overlayDirty = false;
      this.overlayRects = [];
    } else if (this.overlayRects.length) {
      const r = this.overlayRects;
      const x = Math.min(...r.map((v) => v[0])),
        y = Math.min(...r.map((v) => v[1])),
        right = Math.max(...r.map((v) => v[0] + v[2])),
        bottom = Math.max(...r.map((v) => v[1] + v[3]));
      this.drawOverlay([x, y, right - x, bottom - y]);
      this.overlayRects = [];
    }
    if (this.infrastructureDirty || this.infrastructureChanges?.size)
      this.drawInfrastructure();
    if (
      this.store.cars.size ||
      this.store.players.size ||
      this.interactionDirty
    )
      this.drawMotion();
    if (
      this.interactionDirty ||
      this.selected?.kind === "cars" ||
      this.selected?.kind === "trains" ||
      this.selected?.kind === "wagonGroups" ||
      this.selected?.kind === "players"
    ) {
      this.drawInteraction();
      this.interactionDirty = false;
    }
    this.frameWorkMs = performance.now() - start;
    this.maxFrameWorkMs = Math.max(this.maxFrameWorkMs || 0, this.frameWorkMs);
    this.renderMs = this.renderMs * 0.9 + this.frameWorkMs * 0.1;
    this.frames++;
    if (now - this.lastFps > 1000) {
      this.fps = Math.round((this.frames * 1000) / (now - this.lastFps));
      this.frames = 0;
      this.lastFps = now;
      const meters = 100 / this.scale;
      document.getElementById("scale").textContent =
        "100 px ≈ " + number(meters) + " " + t("meters");
    }
  }
}
