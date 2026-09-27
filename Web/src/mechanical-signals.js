// Semantic forms supplied by the optional adapter after checking the actual
// head definition. IsOld alone never selects a semaphore.
export function mechanicalFace(signal, scale) {
  if (
    !["semaphore1", "semaphore2", "discShunting", "discDistant"].includes(
      signal.visualKind,
    )
  )
    return null;
  // Union of real painted extents across stop/clear/restricted/off/unknown.
  // A state change cannot resize the head or move neighbouring sections.
  const bounds =
    signal.visualKind === "semaphore2"
      ? [-13, -24, 13, 25]
      : signal.visualKind === "semaphore1"
        ? [-13, -12, 13, 11]
        : signal.visualKind === "discDistant"
          ? [-12.25, -11, 12.25, 11]
          : [-14, -14, 14, 14];
  return {
    width: (bounds[2] - bounds[0]) * scale,
    height: (bounds[3] - bounds[1]) * scale,
    drawingScale: scale,
    drawingCentre: [(bounds[0] + bounds[2]) / 2, (bounds[1] + bounds[3]) / 2],
    radius: 3 * scale,
    lamps: [],
  };
}

export function drawMechanical(ctx, signal, shape) {
  const main = shape.main,
    kind = signal.visualKind,
    state = signal.visualState,
    known = ["stop", "clear", "restricted", "off"].includes(state);
  ctx.save();
  ctx.translate(main.x, main.y);
  ctx.scale(shape.drawingScale, shape.drawingScale);
  ctx.translate(-shape.drawingCentre[0], -shape.drawingCentre[1]);
  ctx.lineCap = "round";
  ctx.lineJoin = "round";
  ctx.lineWidth = 2;
  if (!known) {
    ctx.strokeStyle = "#a0adb3";
    ctx.setLineDash([2, 2]);
    ctx.strokeRect(-10, -10, 20, 20);
    ctx.setLineDash([]);
    ctx.fillStyle = "#dfe8eb";
    ctx.font = "bold 15px Segoe UI";
    ctx.textAlign = "center";
    ctx.textBaseline = "middle";
    ctx.fillText("?", 0, 0);
  } else if (kind.startsWith("semaphore")) {
    const two = kind === "semaphore2";
    function arm(x, y, raised, vertical) {
      const dx = vertical ? 0 : raised ? 15 : 19,
        dy = vertical ? 10 : raised ? -15 : 0;
      ctx.strokeStyle = "#ef263a";
      ctx.lineWidth = 6;
      ctx.beginPath();
      ctx.moveTo(x, y);
      ctx.lineTo(x + dx, y + dy);
      ctx.stroke();
      ctx.strokeStyle = "#fff";
      ctx.lineWidth = 1.7;
      ctx.stroke();
    }
    if (two) {
      ctx.strokeStyle = "#a4b5bf";
      ctx.lineWidth = 2;
      ctx.beginPath();
      ctx.moveTo(-8, -13);
      ctx.lineTo(-8, 19);
      ctx.stroke();
    }
    arm(-9, two ? -6 : 6, state === "clear" || state === "restricted", false);
    if (two) arm(-9, 12, state === "restricted", state !== "restricted");
    function light(x, y, color) {
      ctx.fillStyle = "#071014";
      ctx.fillRect(x - 4, y - 4, 8, 8);
      ctx.fillStyle = color;
      ctx.beginPath();
      ctx.arc(x, y, 2.8, 0, Math.PI * 2);
      ctx.fill();
    }
    light(
      -9,
      two ? -6 : 6,
      state === "off" ? "#303940" : state === "stop" ? "#ff343f" : "#30f175",
    );
    if (two) light(-9, 12, state === "restricted" ? "#ffcc28" : "#303940");
  } else {
    const distant = kind === "discDistant",
      clear = state === "clear";
    ctx.fillStyle = distant ? "#ffcf00" : "#008cff";
    ctx.strokeStyle = distant ? "#171a1c" : "#fff";
    ctx.lineWidth = 2.5;
    ctx.beginPath();
    if (clear) ctx.rect(-11, -1.5, 22, 3);
    else if (distant) ctx.rect(-10, -9, 20, 18);
    else {
      ctx.moveTo(0, -12);
      ctx.lineTo(12, 0);
      ctx.lineTo(0, 12);
      ctx.lineTo(-12, 0);
      ctx.closePath();
    }
    ctx.fill();
    ctx.stroke();
    if (clear) {
      ctx.fillStyle = "#fff";
      ctx.fillRect(-11, -1.5, 3, 3);
      ctx.fillRect(8, -1.5, 3, 3);
    }
  }
  ctx.restore();
}
