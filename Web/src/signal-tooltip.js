import { preferences } from "./preferences.js";
import { signalHeadLayout } from "./selection-display.js";
import { signalDetailScale } from "./signal-lod.js";
import { drawSignalHead } from "./signal-geometry.js";
import { signalFootprint } from "./signal-footprint.js";
import { aspectText, signalMovement, signalVisible } from "./signal-display.js";
import { entityName, trackName } from "./display-names.js";
import { language, t } from "./localization.js";
import { mapViewport } from "./map-viewport.js";

export function tooltipPosition(pointer, size, viewport, avoid) {
  const [x, y] = pointer,
    [w, h] = size,
    [width, height] = viewport;
  const candidates = [
    [x + 18, y + 18],
    [x - w - 18, y + 18],
    [x + 18, y - h - 18],
    [x - w - 18, y - h - 18],
  ];
  let best;
  for (const candidate of candidates) {
    const px = Math.max(4, Math.min(width - w - 4, candidate[0])),
      py = Math.max(4, Math.min(height - h - 4, candidate[1]));
    const overlap = avoid
      ? Math.max(0, Math.min(px + w, avoid[2]) - Math.max(px, avoid[0])) *
        Math.max(0, Math.min(py + h, avoid[3]) - Math.max(py, avoid[1]))
      : 0;
    const score =
      overlap * 100 + Math.abs(px - candidate[0]) + Math.abs(py - candidate[1]);
    if (!best || score < best.score) best = { x: px, y: py, score };
  }
  return best;
}

// The tooltip reads the same current SignalState as the map, never a copied
// indication or a second polling loop. Its canvas is independent of map layers.
export class SignalTooltip {
  constructor(renderer) {
    this.renderer = renderer;
  }
  hide() {
    if (this.root) this.root.hidden = true;
    this.key = null;
  }
  dispose() {
    this.root?.remove();
    this.root = null;
  }
  update(item, pointer) {
    if (!preferences.signalTooltip || !pointer || item?.kind !== "signals") {
      this.hide();
      return;
    }
    const r = this.renderer,
      signal = r.store.signals.get(item.id);
    if (!signal || !signalVisible(signal, r.layers) || r.panning) {
      this.hide();
      return;
    }
    if (!this.root) {
      if (!document.createElement || !r.root.append) return;
      this.root = document.createElement("div");
      this.root.className = "signal-tooltip";
      this.root.setAttribute("role", "tooltip");
      this.title = document.createElement("strong");
      this.track = document.createElement("span");
      this.aspect = document.createElement("span");
      this.canvas = document.createElement("canvas");
      this.root.append(this.title, this.canvas, this.aspect, this.track);
      r.root.append(this.root);
    }
    const key = [
      signal.id,
      r.store.signalVersions?.get(signal.id) ?? r.store.presentationRevision,
      r.store.topology?.revision,
      r.store.stale,
      language(),
      preferences.signalScale,
      preferences.indicatorScale,
      r.colors?.ink,
      r.colors?.muted,
    ].join(":");
    this.root.hidden = false;
    if (this.key !== key) {
      this.key = key;
      this.title.textContent = entityName(r.store, "signals", signal);
      this.track.textContent = trackName(
        r.store,
        signalMovement(signal, r.store).track,
      );
      this.aspect.textContent =
        aspectText(signal) +
        (r.store.stale ? " · " + t("lastKnownSignal") : "");
      const view = Object.create(r);
      view.signalPreview = true;
      view.scale = signalDetailScale;
      const shape = signalHeadLayout(view, signal);
      shape.angle = shape.bearing = 0;
      const box = signalFootprint(shape),
        width = Math.ceil(box.w + 24),
        height = Math.ceil(box.h + 24);
      const dpr = Math.min(2, globalThis.devicePixelRatio || 1);
      this.canvas.width = Math.ceil(width * dpr);
      this.canvas.height = Math.ceil(height * dpr);
      this.canvas.style.width = width + "px";
      this.canvas.style.height = height + "px";
      const ctx = this.canvas.getContext("2d");
      ctx.scale(dpr, dpr);
      view.project = () => [width / 2 - box.cx, height / 2 - box.cy];
      drawSignalHead(view, ctx, signal, shape, false);
    }
    const area = mapViewport(r);
    if (
      area.width < 60 ||
      pointer[0] < area.left ||
      pointer[0] > area.left + area.width
    ) {
      this.hide();
      return;
    }
    this.root.style.maxWidth = `min(18rem, ${area.width - 8}px)`;
    const avoid = r.signalPaintBounds?.get(signal.id);
    const position = tooltipPosition(
      [pointer[0] - area.left, pointer[1]],
      [this.root.offsetWidth, this.root.offsetHeight],
      [area.width, area.height],
      avoid && [avoid[0] - area.left, avoid[1], avoid[2] - area.left, avoid[3]],
    );
    this.root.style.transform = `translate(${position.x + area.left}px,${position.y}px)`;
  }
}
