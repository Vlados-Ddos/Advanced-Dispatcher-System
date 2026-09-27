import { preferences } from "./preferences.js";
import { signalLod } from "./signal-lod.js";
import { isSignalBoard } from "./railway-objects.js";
export function signalParts(signal) {
  return (signal.parts || [])
    .filter(
      (part) =>
        !isSignalBoard(part) &&
        part.kind !== "namePlate" &&
        (part.kind?.endsWith("Indicator") || !!part.text?.trim()),
    )
    .map((part) => {
      if (part.kind === "departureIndicator")
        return signal.off ? { ...part, active: false, stateKnown: true } : part;
      const visible =
        part.visible !== false &&
        (part.kind?.endsWith("Indicator") && part.kind !== "routeIndicator"
          ? !signal.off && part.active === true && part.stateKnown === true
          : !!part.text?.trim());
      return { ...part, visible };
    });
}
// The supplementary indicators belong to the head that owns them. The
// renderer lays them out as a compact stack under that head; native component
// coordinates remain available in the DTO but never move an indicator onto a
// neighbouring signal.
export function signalComposite(renderer, signal, head) {
  const scale = head.scale,
    gap = 2 * scale,
    main = {
      x: 0,
      y: 0,
      width: head.width + 6 * scale,
      height: head.height + 6 * scale,
    },
    parts = [];
  const visible = [...signalParts(signal)].sort(
    (a, b) => a.order - b.order || a.id.localeCompare(b.id),
  );
  const partScale =
    (preferences.indicatorScale / 100) * signalLod(renderer).size;
  const square = 18 * partScale;
  for (const [index, part] of visible.entries()) {
    const width = square,
      height = square;
    parts.push({
      ...part,
      scale: partScale,
      x:
        (Math.floor(index / 4) - (Math.ceil(visible.length / 4) - 1) / 2) *
        (width + gap),
      y: main.height / 2 + gap + (index % 4) * (height + gap) + height / 2,
      width,
      height,
    });
  }
  const placed = [main, ...parts];
  const left = Math.min(...placed.map((p) => p.x - p.width / 2)),
    right = Math.max(...placed.map((p) => p.x + p.width / 2)),
    top = Math.min(...placed.map((p) => p.y - p.height / 2)),
    bottom = Math.max(...placed.map((p) => p.y + p.height / 2)),
    cx = (left + right) / 2,
    cy = (top + bottom) / 2;
  for (const box of placed) {
    box.x -= cx;
    box.y -= cy;
  }
  return {
    ...head,
    main,
    parts,
    width: right - left,
    height: bottom - top,
    left: (left - right) / 2,
    top: (top - bottom) / 2,
  };
}

function drawIndicatorRing(ctx, part, scale) {
  const radius = Math.min(part.width, part.height) * 0.32;
  for (let i = 0; i < 12; i++) {
    const a = (i / 12) * Math.PI * 2;
    ctx.beginPath();
    ctx.arc(
      part.x + Math.cos(a) * radius,
      part.y + Math.sin(a) * radius,
      1.2 * scale,
      0,
      Math.PI * 2,
    );
    ctx.fillStyle = "#25fa71";
    ctx.fill();
  }
}

export function drawSignalParts(ctx, shape) {
  for (const part of shape.parts || []) {
    if (part.visible === false) continue;
    const x = part.x - part.width / 2,
      y = part.y - part.height / 2;
    ctx.fillStyle = "#050708";
    ctx.strokeStyle = "#93a4ae";
    ctx.lineWidth = 0.7 * (part.scale || shape.scale);
    ctx.beginPath();
    ctx.roundRect(
      x,
      y,
      part.width,
      part.height,
      3 * (part.scale || shape.scale),
    );
    ctx.fill();
    ctx.stroke();
    if (part.kind === "departureIndicator") {
      if (part.stateKnown && part.active)
        drawIndicatorRing(ctx, part, part.scale || shape.scale);
      else if (!part.stateKnown) {
        ctx.fillStyle = "#a3afb7";
        ctx.font = `bold ${12 * (part.scale || shape.scale)}px Segoe UI`;
        ctx.textAlign = "center";
        ctx.textBaseline = "middle";
        ctx.fillText("?", part.x, part.y);
      }
      continue;
    }
    if (!part.text && part.kind === "auxiliaryIndicator") {
      ctx.fillStyle = "#fff";
      ctx.beginPath();
      ctx.arc(part.x, part.y, part.width * 0.16, 0, Math.PI * 2);
      ctx.fill();
    }
    if (part.text) {
      ctx.fillStyle = "#fff";
      ctx.textAlign = "center";
      ctx.textBaseline = "middle";
      ctx.font = `bold ${Math.min(
        13 * (part.scale || shape.scale),
        part.height * 0.8,
        (part.width / Math.max(1, part.text.length)) * 1.5,
      )}px Segoe UI`;
      ctx.fillText(
        part.text,
        part.x,
        part.y,
        part.width - 2 * (part.scale || shape.scale),
      );
    }
  }
}
