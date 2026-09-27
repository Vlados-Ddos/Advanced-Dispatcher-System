import { signalLod, indicatorLod } from "./signal-lod.js";
import { drawMechanical } from "./mechanical-signals.js";
import { drawSignalParts } from "./signal-composite.js";
// Each entry belongs to one actual DV Signals SignalDefinition. Lamp positions
// are taken in that definition's face; array order never means head/direction.
export function lampFace(signal, radius, scale) {
  const physical = signal.lampLayout?.length ? signal.lampLayout : null;
  let lamps;
  if (physical) {
    let spacing = Infinity;
    for (let i = 0; i < physical.length; i++)
      for (let j = i + 1; j < physical.length; j++) {
        const d = Math.hypot(
          physical[i].x - physical[j].x,
          physical[i].y - physical[j].y,
        );
        if (d > 0.01) spacing = Math.min(spacing, d);
      }
    const factor = Number.isFinite(spacing)
      ? (2 * radius + 2 * scale) / spacing
      : 1;
    const xs = physical.map((l) => l.x),
      ys = physical.map((l) => l.y);
    const cx = (Math.min(...xs) + Math.max(...xs)) / 2;
    const cy = (Math.min(...ys) + Math.max(...ys)) / 2;
    lamps = physical.map((l) => ({
      ...l,
      on: signal.off ? false : l.indicationKnown ? l.indicated : l.on,
      blinking: l.indicationKnown ? l.indicationBlinking : l.blinking,
      x: (l.x - cx) * factor,
      y: (cy - l.y) * factor,
    }));
  } else {
    const colors = signal.lamps || [];
    lamps = colors.map((color, i) => ({
      color,
      on: !signal.off,
      blinking: signal.blinkingLamps?.[i] === true,
      x: 0,
      y: (i - (colors.length - 1) / 2) * (2 * radius + 2 * scale),
    }));
  }
  if (!lamps.length)
    return { lamps: [], width: 14 * scale, height: 22 * scale, radius };
  let width =
    Math.max(...lamps.map((l) => l.x)) -
    Math.min(...lamps.map((l) => l.x)) +
    radius * 2 +
    2 * scale;
  let height =
    Math.max(...lamps.map((l) => l.y)) -
    Math.min(...lamps.map((l) => l.y)) +
    radius * 2 +
    2 * scale;
  const fit = Math.min(1, (96 * scale) / Math.max(width, height));
  if (fit < 1) {
    lamps = lamps.map((l) => ({ ...l, x: l.x * fit, y: l.y * fit }));
    width *= fit;
    height *= fit;
    radius *= fit;
  }
  return {
    lamps,
    width,
    height,
    radius,
  };
}

export function directionArrow(shape) {
  if (!Number.isFinite(shape.bearing)) return null;
  const dx = Math.sin(shape.bearing),
    dy = -Math.cos(shape.bearing);
  const local = shape.bearing - (shape.angle || 0);
  // Ray intersection with this head's built-in envelope.
  const edge = Math.min(
    Math.abs(Math.sin(local)) > 1e-9
      ? shape.width / 2 / Math.abs(Math.sin(local))
      : Infinity,
    Math.abs(Math.cos(local)) > 1e-9
      ? shape.height / 2 / Math.abs(Math.cos(local))
      : Infinity,
  );
  const tail = edge + 4 * shape.scale,
    length = 8 * shape.scale;
  return { dx, dy, tail, tip: tail + length, halfWidth: 3 * shape.scale };
}

export function drawSignalConnector(renderer, ctx, signal, shape) {
  const [x, y] = renderer.project(signal.x, signal.z);
  const lod = signalLod(renderer, signal);
  const opacity = lod.opacity * (shape.crowdingOpacity ?? 1);
  if (opacity <= 0.01) return;
  ctx.save();
  ctx.globalAlpha = opacity;
  ctx.translate(x + (shape.offsetX || 0), y + (shape.offsetY || 0));
  const ax = (shape.anchorX ?? 0) - (shape.offsetX || 0),
    ay = (shape.anchorY ?? 0) - (shape.offsetY || 0);
  const cos = Math.cos(shape.angle || 0),
    sin = Math.sin(shape.angle || 0);
  const ray = Math.max(
    Math.abs(ax * cos + ay * sin) / (shape.width / 2),
    Math.abs(-ax * sin + ay * cos) / (shape.height / 2),
  );
  if (ray > 1.05 && lod.connectors > 0) {
    ctx.save();
    ctx.globalAlpha *= lod.connectors;
    ctx.strokeStyle = renderer.colors.muted;
    ctx.lineWidth = 0.8;
    ctx.beginPath();
    ctx.moveTo(ax, ay);
    ctx.lineTo(ax / ray, ay / ray);
    ctx.stroke();
    ctx.fillStyle = renderer.colors.ink;
    ctx.beginPath();
    ctx.arc(ax, ay, 1.6, 0, Math.PI * 2);
    ctx.fill();
    ctx.restore();
  }
  ctx.restore();
}

export function drawSignalHead(renderer, ctx, signal, shape, connector = true) {
  if (connector) drawSignalConnector(renderer, ctx, signal, shape);
  const [x, y] = renderer.project(signal.x, signal.z),
    lod = signalLod(renderer, signal);
  const opacity = lod.opacity * (shape.crowdingOpacity ?? 1);
  if (opacity <= 0.01) return;
  ctx.save();
  ctx.globalAlpha = opacity;
  ctx.translate(x + (shape.offsetX || 0), y + (shape.offsetY || 0));
  ctx.rotate(shape.angle || 0);
  const mechanical = [
    "semaphore1",
    "semaphore2",
    "discShunting",
    "discDistant",
  ].includes(signal.visualKind);
  if (mechanical) drawMechanical(ctx, signal, shape);
  else {
    ctx.save();
    const main = shape.main || {
      x: 0,
      y: 0,
      width: shape.width,
      height: shape.height,
    };
    ctx.translate(main.x, main.y);
    ctx.fillStyle = "#030405";
    ctx.strokeStyle = "#56646c";
    ctx.lineWidth = shape.scale;
    ctx.beginPath();
    const kind =
      signal.visualKind || (signal.shuntingSignal ? "shunting" : "main");
    if (["distant", "repeater"].includes(kind)) {
      const r = Math.min(main.width, main.height) / 2;
      ctx.moveTo(-main.width / 2, -main.height / 2);
      ctx.lineTo(main.width / 2, -main.height / 2);
      ctx.lineTo(main.width / 2, main.height / 2 - r);
      ctx.quadraticCurveTo(
        main.width / 2,
        main.height / 2,
        main.width / 2 - r,
        main.height / 2,
      );
      ctx.lineTo(-main.width / 2 + r, main.height / 2);
      ctx.quadraticCurveTo(
        -main.width / 2,
        main.height / 2,
        -main.width / 2,
        main.height / 2 - r,
      );
      ctx.closePath();
    } else if (kind === "spacing") {
      const cut = Math.min(main.width, main.height) * 0.18;
      ctx.moveTo(-main.width / 2 + cut, -main.height / 2);
      ctx.lineTo(main.width / 2 - cut, -main.height / 2);
      ctx.lineTo(main.width / 2, -main.height / 2 + cut);
      ctx.lineTo(main.width / 2, main.height / 2 - cut);
      ctx.lineTo(main.width / 2 - cut, main.height / 2);
      ctx.lineTo(-main.width / 2 + cut, main.height / 2);
      ctx.lineTo(-main.width / 2, main.height / 2 - cut);
      ctx.lineTo(-main.width / 2, -main.height / 2 + cut);
      ctx.closePath();
    } else if (ctx.roundRect) {
      ctx.roundRect(
        -main.width / 2,
        -main.height / 2,
        main.width,
        main.height,
        kind === "shunting"
          ? 3 * shape.scale
          : Math.min(main.width, main.height) / 2,
      );
    } else ctx.rect(-main.width / 2, -main.height / 2, main.width, main.height);
    ctx.fill();
    ctx.stroke();
    if (!shape.lamps.length) {
      ctx.fillStyle = "#9dabb3";
      ctx.font = `${14 * shape.scale}px Segoe UI`;
      ctx.textAlign = "center";
      ctx.textBaseline = "middle";
      ctx.fillText("?", 0, 0);
    }
    for (const lamp of [...shape.lamps].sort(
      (a, b) => Number(a.on) - Number(b.on),
    )) {
      // phaseOn is captured from LampControl.IsOn, before emission easing.
      // Legacy packets use measured emission without inventing a clock.
      const lit =
        lamp.on &&
        (lamp.phaseKnown
          ? lamp.phaseOn
          : lamp.brightnessKnown
            ? lamp.brightness >= 0.5
            : !lamp.blinking);
      ctx.fillStyle = lit ? lamp.color : "#1c2023";
      ctx.beginPath();
      ctx.arc(lamp.x, lamp.y, shape.radius, 0, Math.PI * 2);
      ctx.fill();
    }
    ctx.restore();
  }
  ctx.save();
  ctx.globalAlpha *= indicatorLod(renderer);
  drawSignalParts(ctx, shape);
  ctx.restore();
  if (signal.reserved) {
    ctx.strokeStyle = renderer.colors.yellow;
    ctx.lineWidth = 1.5;
    ctx.strokeRect(
      -shape.width / 2 - 2,
      -shape.height / 2 - 2,
      shape.width + 4,
      shape.height + 4,
    );
  }
  const arrow = directionArrow(shape);
  if (arrow && lod.detail > 0) {
    ctx.globalAlpha *= lod.detail;
    ctx.rotate(shape.bearing - (shape.angle || 0));
    ctx.strokeStyle = renderer.colors.ink;
    ctx.lineWidth = 1.5 * shape.scale;
    ctx.beginPath();
    ctx.moveTo(0, -arrow.tail);
    ctx.lineTo(0, -arrow.tip);
    ctx.moveTo(-arrow.halfWidth, -arrow.tip + 4 * shape.scale);
    ctx.lineTo(0, -arrow.tip);
    ctx.lineTo(arrow.halfWidth, -arrow.tip + 4 * shape.scale);
    ctx.stroke();
  }
  ctx.restore();
}
