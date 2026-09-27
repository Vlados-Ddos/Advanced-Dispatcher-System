import { signalLod } from "./signal-lod.js";
import { drawMechanical, mechanicalFace } from "./mechanical-signals.js";
import { signalLayout } from "./selection-display.js";
import { isSignalBoard } from "./railway-objects.js";
import { signalVisible } from "./signal-display.js";
import { preferences, mapMetrics } from "./preferences.js";
import { signalAnchor } from "./signal-placement.js";
export function signLayout(renderer, sign) {
  const scale =
    (preferences.signScale / 100) *
    (sign.parentSignal ? signalLod(renderer).size : 1);
  if (!sign.signalObject) {
    const shape = {
      circleRadius: mapMetrics.signRadius * scale,
      width: 2 * mapMetrics.signRadius * scale,
      height: 2 * mapMetrics.signRadius * scale,
      offsetX: 0,
      offsetY: 0,
    };
    const anchor = signalAnchor(sign, renderer.store);
    if (anchor) {
      const offset = shape.circleRadius + 3 * scale;
      shape.offsetX =
        (anchor.x - sign.x) * renderer.scale +
        Math.cos(anchor.bearing) * offset;
      shape.offsetY =
        (sign.z - anchor.z) * renderer.scale +
        Math.sin(anchor.bearing) * offset;
    }
    return shape;
  }
  const mechanical = mechanicalFace(sign, scale);
  if (mechanical)
    return attachedSign(renderer, sign, {
      ...mechanical,
      scale,
      main: { x: 0, y: 0, width: mechanical.width, height: mechanical.height },
    });
  const shape = {
    width: 18 * scale,
    height: 22 * scale,
    scale,
    angle: 0,
  };
  const parent = renderer.store.signals?.get(sign.parentSignal);
  if (parent) {
    const head = signalLayout(renderer, parent),
      visible = signalVisible(parent, renderer.layers);
    const siblings = (parent.parts || [])
      .filter(isSignalBoard)
      .sort((a, b) => a.order - b.order || a.id.localeCompare(b.id));
    let prior = 0;
    for (const part of siblings) {
      if (parent.id + ":" + part.id === sign.id) break;
      prior += 24 * scale;
    }
    shape.angle = head.angle || 0;
    const along =
      (visible ? head.height / 2 : 0) + shape.height / 2 + 2 * scale + prior;
    shape.offsetX =
      (parent.x - sign.x) * renderer.scale +
      (visible ? head.offsetX || 0 : 0) -
      Math.sin(shape.angle) * along;
    shape.offsetY =
      (sign.z - parent.z) * renderer.scale +
      (visible ? head.offsetY || 0 : 0) +
      Math.cos(shape.angle) * along;
  }
  return parent ? shape : attachedSign(renderer, sign, shape);
}
function attachedSign(renderer, sign, shape) {
  const anchor = signalAnchor(sign, renderer.store);
  if (!anchor || ["bufferStop", "turntableIndicator"].includes(sign.signKind))
    return shape;
  const distance = shape.width / 2 + 3 * shape.scale;
  return {
    ...shape,
    angle: anchor.bearing,
    offsetX:
      (anchor.x - sign.x) * renderer.scale +
      Math.cos(anchor.bearing) * distance,
    offsetY:
      (sign.z - anchor.z) * renderer.scale +
      Math.sin(anchor.bearing) * distance,
  };
}
export function drawRailwayBoard(renderer, ctx, sign, shape) {
  ctx.save();
  ctx.translate(...renderer.project(sign.x, sign.z));
  ctx.translate(shape.offsetX || 0, shape.offsetY || 0);
  ctx.rotate(shape.angle || 0);
  if (["discDistant", "discShunting"].includes(sign.visualKind)) {
    drawMechanical(ctx, sign, shape);
    ctx.restore();
    return;
  }
  ctx.fillStyle = "#030506";
  ctx.strokeStyle = renderer.colors.ink;
  ctx.lineWidth = shape.scale;
  const w = shape.width,
    h = shape.height;
  ctx.fillRect(-w / 2, -h / 2, w, h);
  ctx.strokeRect(-w / 2, -h / 2, w, h);
  ctx.fillStyle = "#fff";
  if (sign.signKind === "bufferStop") {
    ctx.beginPath();
    ctx.arc(0, 0, w * 0.4, 0, Math.PI * 2);
    ctx.fill();
    ctx.fillStyle = "#10171d";
    ctx.fillRect(-w / 2, -h * 0.09, w, h * 0.18);
  } else if (sign.signKind === "turntableIndicator") {
    if (sign.turntableStateKnown) {
      if (sign.turntableConnected) {
        ctx.strokeStyle = "#fff";
        ctx.lineWidth = 2 * shape.scale;
        ctx.strokeRect(-w * 0.17, -h * 0.34, w * 0.34, h * 0.68);
      } else {
        ctx.fillStyle = "#008cff";
        ctx.beginPath();
        ctx.moveTo(0, -h * 0.38);
        ctx.lineTo(w * 0.43, 0);
        ctx.lineTo(0, h * 0.38);
        ctx.lineTo(-w * 0.43, 0);
        ctx.closePath();
        ctx.fill();
      }
    } else {
      ctx.font = `${13 * shape.scale}px Segoe UI`;
      ctx.textAlign = "center";
      ctx.textBaseline = "middle";
      ctx.fillText("?", 0, 0);
    }
  } else if (sign.signKind === "distantBoard") {
    for (const side of [-1, 1]) {
      ctx.beginPath();
      ctx.moveTo(side * w * 0.43, -h * 0.42);
      ctx.lineTo(0, 0);
      ctx.lineTo(side * w * 0.43, h * 0.42);
      ctx.closePath();
      ctx.fill();
    }
  } else if (sign.signKind === "distantShort") {
    ctx.beginPath();
    ctx.moveTo(-w * 0.4, -h * 0.2);
    ctx.lineTo(w * 0.4, -h * 0.2);
    ctx.lineTo(0, h * 0.22);
    ctx.closePath();
    ctx.fill();
  } else if (sign.signKind === "shuntLimit") {
    for (let n = 0; n < 5; n++) {
      ctx.fillStyle = n % 2 ? "#fff" : "#078aff";
      ctx.fillRect(-w * 0.14, -h * 0.45 + n * h * 0.18, w * 0.28, h * 0.18);
    }
  } else if (!sign.text) {
    ctx.strokeStyle = "#fff";
    ctx.beginPath();
    ctx.moveTo(0, -h * 0.3);
    ctx.lineTo(w * 0.3, 0);
    ctx.lineTo(0, h * 0.3);
    ctx.lineTo(-w * 0.3, 0);
    ctx.closePath();
    ctx.stroke();
  }
  if (sign.text) {
    ctx.fillStyle = "#fff";
    ctx.textAlign = "center";
    ctx.textBaseline = "middle";
    ctx.font = `${Math.min(13 * shape.scale, (shape.width / Math.max(1, sign.text.length)) * 1.4)}px Segoe UI`;
    ctx.fillText(sign.text, 0, 0, shape.width - 2);
  }
  ctx.restore();
}
