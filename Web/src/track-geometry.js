// Shared segment intersection for signal placement and collision checks.
const intersectionEpsilon = 1e-6;
const cross = (ax, az, bx, bz) => ax * bz - az * bx;
export function intersection(a, b, c, d) {
  const dx = b[0] - a[0],
    dz = b[1] - a[1],
    ex = d[0] - c[0],
    ez = d[1] - c[1];
  const den = cross(dx, dz, ex, ez),
    qx = c[0] - a[0],
    qz = c[1] - a[1];
  if (Math.abs(den) < intersectionEpsilon) {
    if (
      Math.abs(cross(qx, qz, dx, dz)) > intersectionEpsilon ||
      dx * dx + dz * dz < intersectionEpsilon
    )
      return null;
    const u = (qx * dx + qz * dz) / (dx * dx + dz * dz),
      v = u + (ex * dx + ez * dz) / (dx * dx + dz * dz);
    const lo = Math.max(0, Math.min(u, v)),
      hi = Math.min(1, Math.max(u, v));
    return hi >= lo - intersectionEpsilon
      ? {
          x: a[0] + lo * dx,
          z: a[1] + lo * dz,
          overlap: hi - lo > intersectionEpsilon,
        }
      : null;
  }
  const u = cross(qx, qz, ex, ez) / den,
    v = cross(qx, qz, dx, dz) / den;
  return u >= -intersectionEpsilon &&
    u <= 1 + intersectionEpsilon &&
    v >= -intersectionEpsilon &&
    v <= 1 + intersectionEpsilon
    ? { x: a[0] + u * dx, z: a[1] + u * dz, u, v }
    : null;
}
