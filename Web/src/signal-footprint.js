// Rotated envelope of the unchanged head, indicators and attached boards.
// Placement, selection and rendering all use the same local origin.
export function signalFootprint(shape) {
  const a = shape.angle || 0,
    c = Math.cos(a),
    s = Math.sin(a),
    pad = 2 * shape.scale;
  const extra = shape.collisionExtraHeight || 0;
  const arrow = Number.isFinite(shape.bearing) ? 12 * shape.scale : 0;
  const w = Math.max(shape.width, shape.collisionMinWidth || 0) + pad * 2;
  const h = shape.height + extra + arrow + pad * 2;
  return {
    angle: a,
    localWidth: w,
    localHeight: h,
    w: Math.abs(c) * w + Math.abs(s) * h,
    h: Math.abs(s) * w + Math.abs(c) * h,
    cx: (-s * (extra - arrow)) / 2,
    cy: (c * (extra - arrow)) / 2,
  };
}

export function boxSupport(box, nx, ny) {
  const a = box.angle || 0,
    c = Math.cos(a),
    s = Math.sin(a);
  return (
    (Math.abs(nx * c + ny * s) * (box.localWidth ?? box.w) +
      Math.abs(-nx * s + ny * c) * (box.localHeight ?? box.h)) /
    2
  );
}
export function boxesOverlap(a, b) {
  const dx = a.x + a.w / 2 - b.x - b.w / 2,
    dy = a.y + a.h / 2 - b.y - b.h / 2;
  for (const angle of [a.angle || 0, b.angle || 0]) {
    const c = Math.cos(angle),
      s = Math.sin(angle);
    for (const [nx, ny] of [
      [c, s],
      [-s, c],
    ])
      if (
        Math.abs(dx * nx + dy * ny) >=
        boxSupport(a, nx, ny) + boxSupport(b, nx, ny) - 1e-7
      )
        return false;
  }
  return true;
}
