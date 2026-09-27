import { t, number } from "./localization.js";

const aspectKeys = {
  STOP_RESERVED: "aspectReserved",
  STOP_SPECIAL: "aspectSpecial",
  STOP_DETECTED: "aspectDetected",
  RESTRICTED_NEXT_STOP: "aspectRestrictedNextStop",
  NEXT_STOP: "aspectNextStop",
  RESTRICTED_NEXT_RESTRICTED: "aspectRestrictedNextRestricted",
  RESTRICTED: "aspectRestricted",
  NEXT_RESTRICTED: "aspectNextRestricted",
  CLEAR: "aspectProceed",
  STOP: "aspectStop",
  SHUNTING_ALLOWED: "aspectShunting",
  SHUNTING_DISALLOWED: "aspectShuntingDisallowed",
  AGAINST_PATH: "aspectPath",
  RESTRICTED_ENTRY: "aspectRestrictedEntry",
  IS_DEAD_END: "aspectDeadEnd",
  RESERVED_SELF: "aspectOwnReservation",
  SELF_ANY_ASPECT: "aspectDependent",
  DEPARTURE: "aspectDeparture",
};
export function aspectText(signal, index = signal?.aspectIndex) {
  if (!signal) return t("valueUnavailable");
  if ((index === signal.aspectIndex && signal.off) || index < 0)
    return t("off");
  const id = signal.aspects?.[index] ?? signal.aspect;
  const info = signal.aspectInfo?.[index];
  const key =
    info?.reason ||
    aspectKeys[String(id).toUpperCase()] ||
    (info ? (info.stop ? "aspectStop" : "aspectProceed") : "aspectCustom");
  return (
    t(key) +
    (info?.speed > 0 ? " · " + number(info.speed) + " " + t("kmh") : "")
  );
}

// Classification is supplied by the game adapter. Shunting permission is a
// commandable operating state and must never be used as a signal type.
export function signalCategory(signal) {
  if (signal.classificationKnown !== true) return "unknown";
  return signal.shuntingSignal ? "shunting" : "standard";
}
export function signalVisible(signal, layers) {
  if (signal.objectKind === "sign" || signal.displayLayer === "additionalSigns")
    return false;
  return signalCategory(signal) === "shunting"
    ? !!layers.shuntingSignals
    : !!layers.signals;
}

export function signalMovement(signal, store) {
  // JunctionSignalController creates a distinct Block for each head. Placement
  // alone describes the shared mast and cannot identify each outgoing branch.
  const block = store.blocks?.get(signal.block);
  const id = block?.tracks?.[0],
    direction = block?.directions?.[0];
  const track = store.tracks.get(id);
  if (signal.routeBindingRequired && signal.routeBranches?.length === 1) {
    const branch = store.tracks.get(signal.routeBranches[0]);
    const incoming = store.tracks.get(signal.routeIncoming);
    const link = [...(incoming?.a || []), ...(incoming?.b || [])].find(
      (l) => l.track === branch?.id && [0, 1].includes(l.end),
    );
    if (branch && link)
      return {
        ...signal,
        track: signal.routeBranches[0],
        direction: link.end === 0 ? 1 : -1,
        span: link.end === 0 ? 0 : (branch.length ?? branch.spans?.at(-1)),
      };
  }
  if (track && [-1, 1].includes(direction))
    return {
      ...signal,
      track: id,
      direction,
      span:
        id === signal.track
          ? signal.span
          : direction === 1
            ? 0
            : (track.length ?? track.spans?.at(-1)),
    };
  return signal;
}

export function signalControlsLivePath(signal, store) {
  if (!signal.routeBindingRequired) return true;
  if (!signal.routeIncoming || !signal.routeBranches?.length) return false;
  // Exact junction ownership is exported by the native adapter.
  for (const junction of store.junctions?.values() || []) {
    if (junction.incoming !== signal.routeIncoming) continue;
    const branch = store.switches.get(junction.id)?.branch;
    return (
      Number.isInteger(branch) &&
      signal.routeBranches.includes(junction.branches[branch])
    );
  }
  return false;
}

// The arrow follows track travel, not the facing direction of the lamp housing.
export function signalBearing(signal, track) {
  if (!track || ![-1, 1].includes(signal.direction) || track.points.length < 4)
    return null;
  const points = track.points,
    spans = track.spans;
  let at = 0;
  if (spans?.length === points.length / 2) {
    while (at + 2 < spans.length && spans[at + 1] < signal.span) at++;
  } else {
    let nearest = Infinity;
    for (let i = 0; i + 3 < points.length; i += 2) {
      const dx = points[i + 2] - points[i],
        dz = points[i + 3] - points[i + 1];
      const u = Math.max(
        0,
        Math.min(
          1,
          ((signal.x - points[i]) * dx + (signal.z - points[i + 1]) * dz) /
            (dx * dx + dz * dz || 1),
        ),
      );
      const distance =
        (signal.x - points[i] - u * dx) ** 2 +
        (signal.z - points[i + 1] - u * dz) ** 2;
      if (distance < nearest) {
        nearest = distance;
        at = i / 2;
      }
    }
  }
  const dx = (points[at * 2 + 2] - points[at * 2]) * signal.direction;
  const dz = (points[at * 2 + 3] - points[at * 2 + 1]) * signal.direction;
  return dx || dz ? Math.atan2(dx, dz) : null;
}
