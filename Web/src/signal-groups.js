import { signalFootprint } from "./signal-footprint.js";
const compare = (a, b) => (a < b ? -1 : a > b ? 1 : 0);
const shunting = (signal) =>
  signal.classificationKnown === true && signal.shuntingSignal === true;
export const signalPositionKey = (anchor, signal) =>
  anchor
    ? JSON.stringify([anchor.track, anchor.x, anchor.z, anchor.bearing])
    : JSON.stringify(["unbound", signal.id]);

// Only a common native rail attachment and travel direction defines a position.
// A controller may own heads on different branches; it cannot merge those rails.
export function groupSignalEntries(entries) {
  const positions = new Map();
  for (const e of entries) {
    const key = signalPositionKey(e.anchor, e.signal);
    if (!positions.has(key)) positions.set(key, []);
    positions.get(key).push(e);
  }
  for (const group of positions.values()) {
    group.sort(
      (a, b) =>
        Number(shunting(a.signal)) - Number(shunting(b.signal)) ||
        (a.signal.displayOrder || 0) - (b.signal.displayOrder || 0) ||
        compare(a.signal.id, b.signal.id),
    );
    const signature = JSON.stringify(group.map((e) => [e.signal.id, e.key]));
    let bottom = 0;
    const footprints = group.map((e) => signalFootprint(e.shape));
    const halfWidth = Math.max(...footprints.map((f) => f.localWidth)) / 2;
    for (const [i, e] of group.entries()) {
      const f = footprints[i],
        angle = e.shape.angle || 0,
        c = Math.cos(angle),
        s = Math.sin(angle);
      // Stack full head + indicators + attached boards + travel arrow. The
      // first head keeps its origin, additions grow towards local +Y (base).
      const centreY = -s * f.cx + c * f.cy;
      const localY = i
        ? bottom + 4 * e.shape.scale + f.localHeight / 2 - centreY
        : 0;
      bottom = localY + centreY + f.localHeight / 2;
      e.groupId = group[0].signal.id;
      e.groupKey = signature;
      e.groupIndex = i;
      e.stackX = -s * localY;
      e.stackY = c * localY;
      e.groupHalfWidth = halfWidth;
      e.groupCentreX = footprints[0].cx;
      e.groupCentreY = footprints[0].cy;
    }
  }
}

// The collision solver sees one rigid envelope. Members never acquire separate
// separating planes or drift apart when that envelope is projected for zoom.
export function packSignalGroups(entries) {
  const groups = new Map();
  for (const e of entries) {
    const id = e.groupId || e.signal.id;
    if (!groups.has(id)) groups.set(id, []);
    groups.get(id).push(e);
  }
  return [...groups.values()].map((group) => {
    if (group.length === 1) return group[0];
    group.sort((a, b) => a.groupIndex - b.groupIndex);
    const first = group[0],
      angle = first.shape.angle || 0,
      c = Math.cos(angle),
      s = Math.sin(angle);
    let left = Infinity,
      right = -Infinity,
      top = Infinity,
      bottom = -Infinity;
    for (const e of group) {
      const f = signalFootprint(e.shape),
        dx = e.baseX - first.baseX + f.cx,
        dy = e.baseY - first.baseY + f.cy;
      const x = dx * c + dy * s,
        y = -dx * s + dy * c;
      left = Math.min(left, x - f.localWidth / 2);
      right = Math.max(right, x + f.localWidth / 2);
      top = Math.min(top, y - f.localHeight / 2);
      bottom = Math.max(bottom, y + f.localHeight / 2);
    }
    const x = (left + right) / 2,
      y = (top + bottom) / 2;
    const baseX = first.baseX + c * x - s * y,
      baseY = first.baseY + s * x + c * y;
    const shape = {
      ...first.shape,
      width: right - left - 4 * first.shape.scale,
      height: bottom - top - 4 * first.shape.scale,
      bearing: null,
      collisionExtraHeight: 0,
      collisionMinWidth: 0,
      offsetX: baseX - first.signal.x * first.reference,
      offsetY: baseY + first.signal.z * first.reference,
    };
    const members = group.map((e) => ({
      id: e.signal.id,
      x: e.baseX - baseX,
      y: e.baseY - baseY,
    }));
    return {
      ...first,
      baseX,
      baseY,
      shape,
      members,
      clearance: (right - left) / 2 + first.corridor,
      priorX: first.priorX,
      priorY: first.priorY,
    };
  });
}
