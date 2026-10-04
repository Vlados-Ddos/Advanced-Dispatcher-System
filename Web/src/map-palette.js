// Operational meanings, shared by route/rail/turnout drawing and the legend.
// Occupancy remains red. Reservation and selected branch never reuse red.
export const mapPalette = Object.freeze({
  branchBorder: "#1c426d",
  selected: "#47ceff",
  route: "#efc66a",
  routePreview: "#65b9ff",
  routeAffected: "#ff9f43",
  reservation: "#c69cff",
  protectedReservation: "#b58bff",
  protectionFootprint: "#8f78c7",
});

// A parallel annotation stroke keeps occupancy on the actual centreline.
export function offsetPolyline(points, offset) {
  return points.map((p, i) => {
    const a = points[Math.max(0, i - 1)],
      b = points[Math.min(points.length - 1, i + 1)],
      dx = b[0] - a[0],
      dy = b[1] - a[1],
      length = Math.hypot(dx, dy) || 1;
    return [p[0] - (dy / length) * offset, p[1] + (dx / length) * offset];
  });
}
