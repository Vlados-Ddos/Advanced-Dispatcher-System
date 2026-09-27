// Map coordinates are metres. At 24 px/m a 20 m locomotive spans 480 px;
// rail geometry remains useful without magnifying screen-sized badges.
import { mapViewport } from "./map-viewport.js";
export const zoomLimits = Object.freeze({ min: 0.005, max: 24, focus: 2 });
export const clampZoom = (scale) =>
  Math.max(zoomLimits.min, Math.min(zoomLimits.max, scale));

export function beginFocus(renderer, item, zoom = zoomLimits.focus) {
  const target = renderer.focusPosition(item);
  if (!target) return;
  zoom = target.zoom ?? clampZoom(Math.max(renderer.scale, zoom));
  const point = renderer.focusPosition(item, zoom);
  renderer.follow = null;
  renderer.cameraFocus = {
    item: { ...item },
    start: performance.now(),
    x: renderer.cx,
    z: renderer.cz,
    scale: renderer.scale,
    zoom: clampZoom(zoom),
    point,
    width: renderer.width,
    height: renderer.height,
    area: mapViewport(renderer),
  };
}

export function advanceFocus(renderer, now) {
  const f = renderer.cameraFocus;
  if (!f) return;
  const area=mapViewport(renderer);
  // Opening the inspector can resize the viewport after selection. Keep the
  // active focus target fitted to the final viewport without restarting pan.
  if (f.width !== renderer.width || f.height !== renderer.height || f.area?.left!==area.left || f.area?.width!==area.width) {
    const target = renderer.focusPosition(f.item, f.zoom);
    if (target) {
      f.zoom = clampZoom(target.zoom ?? f.zoom);
      f.point = renderer.focusPosition(f.item, f.zoom);
    }
    f.width = renderer.width;
    f.height = renderer.height;
    f.area = area;
  }
  const moving = ["cars", "trains", "wagonGroups", "players"].includes(
    f.item.kind,
  );
  const placing = f.item.kind === "signals";
  const pending =
    placing &&
    (renderer.signalWork ||
      renderer.signalPlacementPlan?.byId?.get(f.item.id)?.pending ||
      renderer.signalPlacementPlan?.byId?.get(f.item.id)?.preparing);
  if (f.waitingForLayout && !pending) {
    f.waitingForLayout = false;
    f.start = now;
    f.x = renderer.cx;
    f.z = renderer.cz;
    f.scale = renderer.scale;
  }
  const available = renderer.resolve
    ? renderer.resolve(f.item)
    : renderer.focusPosition(f.item);
  const p = available
    ? moving || placing
      ? renderer.focusPosition(f.item, f.zoom)
      : f.point
    : null;
  if (!p || !Number.isFinite(p.x) || !Number.isFinite(p.z)) {
    renderer.cameraFocus = null;
    return;
  }
  const u = Math.min(1, Math.max(0, (now - f.start) / 360));
  const ease = u * u * (3 - 2 * u);
  renderer.scale = Math.exp(
    Math.log(f.scale) + (Math.log(f.zoom) - Math.log(f.scale)) * ease,
  );
  const canvasWidth=Number.isFinite(renderer.width)?renderer.width:0;
  const targetX=p.x-(area.left+area.width/2-canvasWidth/2)/renderer.scale;
  renderer.cx = f.x + (targetX - f.x) * ease;
  renderer.cz = f.z + (p.z - f.z) * ease;
  renderer.invalidate();
  if (u === 1) {
    if (pending) {
      f.waitingForLayout = true;
      return;
    }
    renderer.cameraFocus = null;
    if (["cars", "trains", "wagonGroups", "players"].includes(f.item.kind))
      renderer.follow = f.item;
  }
}
