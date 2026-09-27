import { aboveLod } from "./lod.js";
import { destinationColor } from "./job-display.js";
import { railwaySigns, signVisible } from "./railway-objects.js";
import { signLayout, drawRailwayBoard } from "./sign-geometry.js";
import { entityName, locationName } from "./display-names.js";
import { allLocations, locationVisible } from "./locations.js";
import { t, number } from "./localization.js";
import { preferences, mapMetrics } from "./preferences.js";
import { speedSections, signSpeed } from "./operations.js";

function pointsAlong(track) {
  let total = 0;
  const points = [];
  for (let i = 0; i < track.points.length; i += 2) {
    if (i)
      total += Math.hypot(
        track.points[i] - track.points[i - 2],
        track.points[i + 1] - track.points[i - 1],
      );
    points.push({
      x: track.points[i],
      z: track.points[i + 1],
      span: track.spans?.[i / 2] ?? total,
    });
  }
  if (!track.spans?.length && total)
    for (const p of points) p.span = (p.span / total) * track.length;
  return points;
}
export function sectionPoints(track, from, to) {
  const points = pointsAlong(track),
    low = Math.min(from, to),
    high = Math.max(from, to);
  function at(span) {
    for (let i = 1; i < points.length; i++)
      if (points[i].span >= span) {
        const a = points[i - 1],
          b = points[i],
          u = Math.max(
            0,
            Math.min(1, (span - a.span) / (b.span - a.span || 1)),
          );
        return { x: a.x + (b.x - a.x) * u, z: a.z + (b.z - a.z) * u };
      }
    return points.at(-1);
  }
  const result = [
    at(low),
    ...points.filter((p) => p.span > low && p.span < high),
    at(high),
  ];
  return from > to ? result.reverse() : result;
}
export function prepareSpeedPaths(renderer) {
  renderer.speedPaths = speedSections(renderer.store).map((s) => {
    const points = sectionPoints(
        renderer.store.tracks.get(s.track),
        s.from,
        s.to,
      ),
      path = new Path2D();
    points.forEach((p, i) =>
      i ? path.lineTo(p.x, -p.z) : path.moveTo(p.x, -p.z),
    );
    return { ...s, path, middle: points[Math.floor(points.length / 2)] };
  });
}
export function drawDetails(r, ctx, pass = "all") {
  const visible = (p, margin = 50) =>
    p[0] >= -margin &&
    p[1] >= -margin &&
    p[0] <= r.width + margin &&
    p[1] <= r.height + margin;
  const occupiedLabels = [...(r.trackLabelBoxes || [])];
  const label = (text, x, y, size, color = r.colors.muted) => {
    ctx.font = `${size}px Segoe UI`;
    const limit = Math.min((320 * preferences.labelScale) / 100, r.width - 16);
    if (ctx.measureText(text).width > limit) {
      let low = 0,
        high = text.length;
      while (low < high) {
        const mid = Math.ceil((low + high) / 2);
        if (ctx.measureText(text.slice(0, mid) + "…").width <= limit) low = mid;
        else high = mid - 1;
      }
      text = text.slice(0, low) + "…";
    }
    const width = ctx.measureText(text).width;
    const box = [x - width / 2 - 4, y - size - 3, x + width / 2 + 4, y + 3];
    if (
      occupiedLabels.some(
        (b) => box[0] < b[2] && box[2] > b[0] && box[1] < b[3] && box[3] > b[1],
      )
    )
      return;
    occupiedLabels.push(box);
    ctx.textAlign = "center";
    ctx.lineWidth = 3;
    ctx.strokeStyle = r.colors.map;
    ctx.strokeText(text, x, y);
    ctx.fillStyle = color;
    ctx.fillText(text, x, y);
  };
  if (
    pass !== "foreground" &&
    (r.layers.speedLimits || r.layers.speedRestrictions)
  ) {
    if (!r.speedPaths) prepareSpeedPaths(r);
    const ids = new Set(r.visible.map((x) => x.id));
    for (const s of r.speedPaths) {
      if (!ids.has(s.track)) continue;
      if (r.layers.speedRestrictions) {
        ctx.save();
        r.worldTransform(ctx);
        ctx.globalAlpha = 0.22;
        ctx.strokeStyle =
          s.speed <= 40
            ? r.colors.red
            : s.speed <= 60
              ? r.colors.yellow
              : r.colors.green;
        ctx.lineWidth = 8 / r.scale;
        ctx.stroke(s.path);
        ctx.restore();
      }
      if (
        r.layers.speedLimits &&
        aboveLod(r, "lodSpeedLabels", 0.18) &&
        s.middle
      ) {
        const p = r.project(s.middle.x, s.middle.z);
        if (visible(p))
          label(
            `${s.direction > 0 ? "→" : "←"} ${s.speed} ${t("kmh")}`,
            p[0],
            p[1] + 18,
            (mapMetrics.label * preferences.labelScale) / 100,
          );
      }
    }
  }
  if (
    pass !== "background" &&
    (r.layers.stationMarkers ||
      r.layers.industryMarkers ||
      r.layers.passengerStops)
  )
    for (const s of allLocations(r.store)) {
      if (!locationVisible(s, r.layers)) continue;
      const p = r.project(s.x, s.z);
      if (!visible(p)) continue;
      ctx.fillStyle = destinationColor(r.store, s.id);
      ctx.fillRect(p[0] - 3, p[1] - 3, 6, 6);
      label(
        (s.industry ? "▥ " : "") + locationName(r.store, s),
        p[0],
        p[1] - 12,
        (mapMetrics.stationLabel * preferences.labelScale) / 100,
        destinationColor(r.store, s.id),
      );
    }
  if (
    pass !== "background" &&
    r.layers.blockLabels &&
    aboveLod(r, "lodBlockLabels", 0.2)
  ) {
    const seen = new Set();
    for (const b of r.store.blocks.values()) {
      const key = [...b.tracks].sort().join("|");
      if (seen.has(key)) continue;
      seen.add(key);
      const track = r.store.tracks.get(
        b.tracks[Math.floor(b.tracks.length / 2)],
      );
      if (!track) continue;
      const i = Math.floor(track.points.length / 4) * 2,
        p = r.project(track.points[i], track.points[i + 1]);
      if (visible(p))
        label(
          entityName(r.store, "blocks", b),
          p[0],
          p[1] + 28,
          (mapMetrics.label * preferences.blockLabelScale) / 100,
          b.occupied ? r.colors.red : r.colors.muted,
        );
    }
  }
  if (
    pass !== "background" &&
    (r.layers.signs || r.layers.additionalSigns) &&
    aboveLod(r, "lodSigns", 0.15)
  )
    for (const s of railwaySigns(r.store)) {
      if (!signVisible(s, r.layers)) continue;
      const p = r.project(s.x, s.z);
      if (!visible(p)) continue;
      if (s.signalObject) {
        drawRailwayBoard(r, ctx, s, signLayout(r, s));
        continue;
      }
      ctx.save();
      const shape = signLayout(r, s);
      ctx.translate(p[0] + (shape.offsetX || 0), p[1] + (shape.offsetY || 0));
      const size = (mapMetrics.signRadius * preferences.signScale) / 100;
      ctx.fillStyle = r.colors.map;
      ctx.strokeStyle = s.advance ? r.colors.yellow : r.colors.muted;
      ctx.lineWidth = 1;
      ctx.beginPath();
      ctx.arc(0, 0, size, 0, Math.PI * 2);
      ctx.fill();
      ctx.stroke();
      ctx.font = `${(mapMetrics.signLabel * preferences.signScale) / 100}px Segoe UI`;
      ctx.fillStyle = r.colors.ink;
      ctx.textAlign = "center";
      ctx.fillText(
        s.speeds.length ? (signSpeed(s, r.store) ?? s.speeds.join("/")) : "◇",
        0,
        3,
      );
      ctx.restore();
    }
  if (
    pass !== "background" &&
    r.layers.technical &&
    aboveLod(r, "lodTechnical", 0.4)
  )
    for (const item of r.visible) {
      const track = r.store.tracks.get(item.id),
        p = r.project(track.points[0], track.points[1]);
      if (visible(p))
        label(
          track.id + " · " + number(track.length) + " " + t("meters"),
          p[0],
          p[1] - 18,
          (mapMetrics.signLabel * preferences.signScale) / 100,
        );
    }
}
