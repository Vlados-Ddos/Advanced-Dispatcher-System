// Common optical extent: the existing three-lamp face is the reference.
// Preserve aspect ratio, circles and lamp spacing. Padding and supplementary
// indicators keep their own common scale; no per-track fit or per-type zoom.
export const signalFaceExtent = 39;
export function normalizeSignalFace(face, scale) {
  // Electrical heads keep the same lamp diameter. A one/two-lamp head reserves
  // the normal body height instead of inflating its lamp into a wide square.
  if (face.drawingScale == null)
    face = { ...face, height: Math.max(face.height, signalFaceExtent * scale) };
  const factor = (signalFaceExtent * scale) / Math.max(face.width, face.height);
  return {
    ...face,
    width: face.width * factor,
    height: face.height * factor,
    radius: face.radius * factor,
    drawingScale:
      face.drawingScale == null ? undefined : face.drawingScale * factor,
    lamps: face.lamps.map((l) => ({ ...l, x: l.x * factor, y: l.y * factor })),
  };
}
