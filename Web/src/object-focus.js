import { routeLines } from "./route-display.js";
import { signalLayout } from "./selection-display.js";
import { signLayout } from "./sign-geometry.js";
import { zoomLimits, clampZoom } from "./camera.js";
import { signalDetailScale } from "./signal-lod.js";
import { mapViewport } from "./map-viewport.js";

export function objectFocus(renderer, item, zoom = renderer.scale) {
  const area = mapViewport(renderer);
  const object = renderer.resolve(item, renderer.store);
  if (!object) return null;
  const store = renderer.store;
  if (item.kind === "cars" || item.kind === "players") {
    const p =
      item.kind === "cars"
        ? store.position(object, renderer.frameTime)
        : store.playerPosition(object, renderer.frameTime);
    return {
      ...p,
      zoom: clampZoom(Math.max(renderer.scale, zoomLimits.focus)),
    };
  }
  const points = [];
  if (item.kind === "trains" || item.kind === "wagonGroups") {
    for (const id of object.carIds || []) {
      const c = store.cars.get(id);
      if (!c) continue;
      const p = store.position(c, renderer.frameTime),
        radius = (c.length ?? 0) / 2;
      points.push([p.x - radius, p.z - radius], [p.x + radius, p.z + radius]);
    }
  } else if (item.kind === "routes") {
    for (const line of routeLines(object, store))
      for (let i = 0; i < line.length; i += 2)
        points.push([line[i], line[i + 1]]);
  } else if (["blocks", "tracks"].includes(item.kind)) {
    const ids =
      item.kind === "tracks"
        ? [object.id]
        : [...(object.tracks || []), ...(object.extraTracks || [])];
    for (const id of new Set(ids)) {
      const p = store.tracks.get(id)?.points || [];
      for (let i = 0; i < p.length; i += 2) points.push([p[i], p[i + 1]]);
    }
  }
  if (points.length) {
    const x0 = Math.min(...points.map((p) => p[0])),
      x1 = Math.max(...points.map((p) => p[0])),
      z0 = Math.min(...points.map((p) => p[1])),
      z1 = Math.max(...points.map((p) => p[1]));
    return {
      x: (x0 + x1) / 2,
      z: (z0 + z1) / 2,
      zoom: clampZoom(
        Math.min(
          zoomLimits.focus,
          Math.max(80, area.width - 120) / Math.max(1, x1 - x0),
          Math.max(80, area.height - 120) / Math.max(1, z1 - z0),
        ),
      ),
    };
  }
  if (!Number.isFinite(object.x) || !Number.isFinite(object.z)) return null;
  const targetZoom = clampZoom(
    Math.max(
      renderer.scale,
      item.kind === "signals" ? signalDetailScale : zoomLimits.focus,
    ),
  );
  if (["signals", "signs"].includes(item.kind)) {
    const view = Object.create(renderer);
    view.scale = zoom;
    const shape =
      item.kind === "signals"
        ? signalLayout(view, object)
        : signLayout(view, object);
    return {
      x: object.x + (shape.offsetX || 0) / zoom,
      z: object.z - (shape.offsetY || 0) / zoom,
      zoom: targetZoom,
    };
  }
  return { x: object.x, z: object.z, zoom: targetZoom };
}
