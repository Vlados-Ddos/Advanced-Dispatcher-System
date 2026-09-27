import { mapPalette } from "./map-palette.js";
import { preferences } from "./preferences.js";
import { junctionLod } from "./junction-lod.js";

function endpointLeg(track, end) {
  if ((end !== 0 && end !== 1) || !track?.points || track.points.length < 4)
    return null;
  const points = [];
  for (
    let i = end === 0 ? 0 : track.points.length - 2;
    i >= 0 && i < track.points.length;
    i += end === 0 ? 2 : -2
  )
    points.push([track.points[i], track.points[i + 1]]);
  return points;
}

export function junctionGeometry(junction, tracks) {
  const incoming = tracks.get(junction.incoming);
  const branches = (junction.branches || []).map(() => null);
  let incomingEnd = null;
  // Use graph endpoint links. Coincident/nearby rails are never connections.
  for (const end of [0, 1])
    for (const link of (end === 0 ? incoming?.a : incoming?.b) || []) {
      if (
        link.junction !== junction.id ||
        junction.branches[link.branch] !== link.track
      )
        continue;
      if (incomingEnd !== null && incomingEnd !== end)
        return { incoming: null, branches: branches.map(() => null) };
      incomingEnd = end;
      branches[link.branch] = endpointLeg(tracks.get(link.track), link.end);
    }
  return {
    incoming: incomingEnd === null ? null : endpointLeg(incoming, incomingEnd),
    branches,
  };
}

export function selectedBranch(
  junction,
  state,
  stale = false,
  geometry = null,
) {
  const branch = state?.branch;
  if (
    stale ||
    !Number.isInteger(branch) ||
    branch < 0 ||
    branch >= (junction.branches?.length || 0) ||
    !junction.branches[branch]
  )
    return -1;
  if (geometry && (!geometry.incoming || !geometry.branches[branch])) return -1;
  return branch;
}

export function clipLeg(points, length) {
  if (!points?.length) return [];
  const result = [points[0]];
  for (let i = 1; i < points.length && length > 0; i++) {
    const a = points[i - 1],
      b = points[i],
      distance = Math.hypot(b[0] - a[0], b[1] - a[1]);
    if (distance > length) {
      result.push([
        a[0] + ((b[0] - a[0]) * length) / distance,
        a[1] + ((b[1] - a[1]) * length) / distance,
      ]);
      break;
    }
    result.push(b);
    length -= distance;
  }
  return result;
}

// A fixed distance in world metres avoids zoom-dependent rotations and the
// near-identical first (often kinked) samples at the toe of a curved turnout.
export function branchDirection(points) {
  const line = clipLeg(points, 18);
  if (line.length < 2) return null;
  const a = line[0],
    b = line.at(-1);
  const dx = b[0] - a[0],
    dz = b[1] - a[1];
  return Math.hypot(dx, dz) > 0.01 ? Math.atan2(-dz, dx) : null;
}

export const junctionPadding = 176;

export function junctionDirection(geometry, branch) {
  const incoming = branchDirection(geometry?.incoming);
  if (incoming === null || incoming === undefined || branch < 0) return null;
  const forward = incoming + Math.PI;
  const angles = geometry.branches.map((points) => {
    const direction = branchDirection(points);
    return direction === null
      ? null
      : Math.atan2(
          Math.sin(direction - forward),
          Math.cos(direction - forward),
        );
  });
  const angle = angles[branch];
  if (angle === null || angle === undefined) return null;
  const others = angles.filter((x, i) => x !== null && i !== branch);
  const straight =
    Math.abs(angle) < 0.002 ||
    (others.length > 0 &&
      Math.abs(angle) < 0.26 &&
      others.every((x) => Math.abs(x) > Math.abs(angle) + 0.003));
  return forward + (straight ? 0 : (Math.sign(angle) * Math.PI) / 2);
}

export function junctionBadge(renderer, junction) {
  const geometry = renderer.junctionGeometry.get(junction.id);
  const branch = selectedBranch(
    junction,
    renderer.store.switches.get(junction.id),
    renderer.store.stale,
    geometry,
  );
  const direction = branch >= 0 ? junctionDirection(geometry, branch) : null;
  // Rotate the complete badge, including its frame and stem.
  const angle = direction;
  const lod = junctionLod(renderer);
  const size = (8.5 * preferences.switchScale * lod.size) / 100;
  return {
    lod,
    size,
    diamond: false,
    width: size * 2.9,
    height: size * 2.9,
    angle,
    direction,
    forward: geometry?.incoming
      ? branchDirection(geometry.incoming) + Math.PI
      : null,
    branch,
  };
}

export function drawJunction(renderer, ctx, junction, pass = "all") {
  const [x, y] = renderer.project(junction.x, junction.z);
  const view = renderer.view?.();
  const branchesVisible =
    pass !== "foreground" &&
    renderer.layers.switchBranches &&
    (!view ||
      (junction.branches || []).some((id) => {
        const box = renderer.paths.get(id)?.box;
        return (
          box &&
          box[0] <= view[2] &&
          box[2] >= view[0] &&
          box[1] <= view[3] &&
          box[3] >= view[1]
        );
      }));
  if (
    !branchesVisible &&
    (x < -junctionPadding ||
      y < -junctionPadding ||
      x > renderer.width + junctionPadding ||
      y > renderer.height + junctionPadding)
  )
    return;
  const geometry = renderer.junctionGeometry.get(junction.id);
  const branch = geometry
    ? selectedBranch(
        junction,
        renderer.store.switches.get(junction.id),
        renderer.store.stale,
        geometry,
      )
    : -1;
  const branchScale = preferences.branchScale / 100;
  const reach = Math.min(160, Math.min(150, 88 * branchScale) / renderer.scale);
  const direction = branch >= 0 ? junctionDirection(geometry, branch) : null;
  const stroke = (line, color, width, dashed = false) => {
    if (line.length < 2) return;
    ctx.beginPath();
    line
      .map((point) => renderer.project(...point))
      .forEach((p, i) => {
        if (i) ctx.lineTo(...p);
        else ctx.moveTo(...p);
      });
    ctx.strokeStyle = color;
    ctx.lineWidth = width;
    ctx.lineCap = "round";
    ctx.lineJoin = "round";
    ctx.setLineDash(dashed ? [4 * branchScale, 3 * branchScale] : []);
    ctx.stroke();
  };
  ctx.save();
  if (pass !== "foreground" && geometry && renderer.layers.switchBranches) {
    // Paint every actual branch first. The inactive branch remains visible on
    // both themes, including where its initial tangent overlaps the active one.
    for (let i = 0; i < geometry.branches.length; i++) {
      if (i === branch && direction !== null) continue;
      const line = geometry.branches[i] || [];
      stroke(line, "#392a19", 3.2 * branchScale);
      stroke(line, "#efc66a", 1.5 * branchScale, true);
    }
    if (direction !== null)
      for (const leg of [geometry.incoming, geometry.branches[branch]]) {
        const line = clipLeg(
          leg,
          leg === geometry.incoming ? reach * 0.55 : Infinity,
        );
        const trackId =
          leg === geometry.incoming
            ? junction.incoming
            : junction.branches[branch];
        const occupied =
          renderer.layers.blocks &&
          (renderer.store.occupancy?.get(trackId)?.occupied ||
            renderer.blockVisual?.get(trackId)?.occupied);
        // Exact centreline. On occupied rails use a dashed annotation so the
        // red occupancy evidence remains visible between the white segments.
        stroke(line, mapPalette.branchBorder, 3.8 * branchScale, occupied);
        stroke(line, preferences.branchColor, 1.8 * branchScale, occupied);
      }
  }
  ctx.setLineDash([]);
  if (pass === "background" || !renderer.layers.switches) {
    ctx.restore();
    return;
  }
  const badge = junctionBadge(renderer, junction);
  const { size, lod } = badge;
  if (lod.opacity <= 0.01) {
    ctx.restore();
    return;
  }
  ctx.globalAlpha *= lod.opacity;
  ctx.translate(x, y);
  if (badge.angle !== null) ctx.rotate(badge.angle);
  ctx.fillStyle = "#f3131b";
  ctx.strokeStyle = "#ffffff";
  ctx.lineWidth = Math.max(0.9, size * 0.13);
  const radius = badge.width / 2;
  ctx.beginPath();
  ctx.roundRect(-radius, -radius, radius * 2, radius * 2, size * 0.55);
  ctx.fill();
  ctx.save();
  ctx.globalAlpha *= lod.detail;
  ctx.stroke();
  ctx.restore();
  ctx.globalAlpha *= lod.arrow;
  if (direction === null) {
    ctx.fillStyle = "#ffffff";
    ctx.font = `bold ${Math.max(9, size * 1.35)}px Segoe UI`;
    ctx.textAlign = "center";
    ctx.textBaseline = "middle";
    ctx.fillText("?", 0, 0);
  } else {
    const directions = geometry.branches.map((_, i) =>
      junctionDirection(geometry, i),
    );
    const ambiguous = directions.some(
      (v, i) =>
        i !== branch &&
        v !== null &&
        Math.abs(Math.atan2(Math.sin(v - direction), Math.cos(v - direction))) <
          0.01,
    );
    const complex = ambiguous || geometry.branches.length > 3;
    if (complex && lod.detail > 0) {
      // Complex configurations retain actual exit bearings inside the badge.
      for (let i = 0; i < geometry.branches.length; i++) {
        const a = branchDirection(geometry.branches[i]);
        if (a === null) continue;
        ctx.save();
        ctx.rotate(a - direction);
        ctx.strokeStyle = "#ffffff";
        ctx.globalAlpha *= lod.detail * (i === branch ? 1 : 0.4);
        ctx.lineWidth = size * (i === branch ? 0.36 : 0.12);
        ctx.beginPath();
        ctx.moveTo(0, 0);
        ctx.lineTo(size * 1.05, 0);
        ctx.stroke();
        ctx.restore();
      }
    }
    if (!complex || lod.detail < 1) {
      ctx.save();
      if (complex) ctx.globalAlpha *= 1 - lod.detail;
      ctx.strokeStyle = "#ffffff";
      ctx.fillStyle = "#ffffff";
      ctx.lineWidth = size * 0.4;
      ctx.lineCap = "round";
      ctx.beginPath();
      ctx.moveTo(-size * 0.95, 0);
      ctx.lineTo(size * 0.45, 0);
      ctx.stroke();
      ctx.beginPath();
      ctx.moveTo(size * 1.05, 0);
      ctx.lineTo(size * 0.22, -size * 0.64);
      ctx.lineTo(size * 0.22, size * 0.64);
      ctx.closePath();
      ctx.fill();
      ctx.restore();
    }
  }
  ctx.restore();
}
