import { isLocomotive } from "./rolling-stock.js";
import { signalControlsLivePath } from "./signal-display.js";
// Derived browser views. No commands or independent simulation of gameplay state.
export function jobElapsed(job, store, now = Date.now() + store.serverOffset) {
  if (!job.active) return Math.max(0, job.elapsedSeconds || 0);
  const caps = store.capabilities;
  const sinceSample = store.stale
    ? 0
    : Math.max(0, Math.min(2, (now - (caps.sampledAt || now)) / 1000));
  const gameTime =
    (caps.gameTime ?? job.sampledGameTime ?? 0) +
    sinceSample * (caps.clockRate || 0);
  return Math.max(
    0,
    (job.elapsedSeconds || 0) +
      Math.max(0, gameTime - (job.sampledGameTime || 0)),
  );
}
export function duration(seconds) {
  if (!Number.isFinite(seconds)) return "—";
  seconds = Math.max(0, Math.floor(seconds));
  const h = Math.floor(seconds / 3600),
    m = Math.floor((seconds % 3600) / 60),
    s = seconds % 60;
  return (
    (h ? `${h}:` : "") +
    (h ? String(m).padStart(2, "0") : m) +
    ":" +
    String(s).padStart(2, "0")
  );
}
export function jobTrains(job, store) {
  const result = new Set();
  for (const id of job.cars || []) {
    const c = store.cars.get(id);
    if (c?.consist) result.add(c.consist);
  }
  return [...result];
}
export function trainBlocks(train, store) {
  return [...store.blocks.values()].filter((b) => b.trains?.includes(train));
}
export function playersAboard(train, store) {
  return [...store.players.values()].filter(
    (p) => store.cars.get(p.car)?.consist === train,
  );
}
export function signalAhead(car, store, maxDistance = 5000) {
  if (!car?.direction || !car.track1) return null;
  let track = car.track1,
    direction = car.direction,
    origin = car.span1 || 0,
    distance = 0;
  const visited = new Set();
  while (distance <= maxDistance && !visited.has(track + ":" + direction)) {
    visited.add(track + ":" + direction);
    let candidate = null;
    for (const s of store.signals.values()) {
      const gap = direction * (s.span - origin);
      if (
        s.objectKind !== "sign" &&
        signalControlsLivePath(s, store) &&
        s.track === track &&
        s.direction === direction &&
        gap >= 0 &&
        (!candidate || gap < candidate.distance)
      )
        candidate = { signal: s, distance: gap };
    }
    if (candidate)
      return { ...candidate, distance: candidate.distance + distance };
    const t = store.tracks.get(track);
    if (!t) return null;
    distance += direction > 0 ? t.length - origin : origin;
    const links = (direction > 0 ? t.b : t.a).filter(
      (l) => !l.junction || store.switches.get(l.junction)?.branch === l.branch,
    );
    if (links.length !== 1) return null;
    track = links[0].track;
    direction = links[0].end === 0 ? 1 : -1;
    origin = direction > 0 ? 0 : store.tracks.get(track)?.length || 0;
  }
  return null;
}
export function signSpeed(sign, store) {
  if (sign.advance) return null;
  for (let i = 0; i < (sign.speeds?.length || 0); i++) {
    const branch = sign.branches?.[i] ?? -1;
    if (branch < 0 || store.switches.get(sign.junction)?.branch === branch)
      return sign.speeds[i];
  }
  return null;
}
export function speedSections(store) {
  const groups = new Map(),
    result = [];
  for (const sign of store.signs.values()) {
    if (sign.advance || !sign.speeds?.length) continue;
    const key = sign.track + ":" + sign.direction;
    if (!groups.has(key)) groups.set(key, []);
    groups.get(key).push(sign);
  }
  for (const signs of groups.values()) {
    signs.sort((a, b) => a.direction * (a.span - b.span));
    for (let i = 0; i < signs.length; i++) {
      const sign = signs[i],
        track = store.tracks.get(sign.track),
        speed = signSpeed(sign, store);
      if (!track || speed === null) continue;
      result.push({
        track: sign.track,
        from: sign.span,
        to: signs[i + 1]?.span ?? (sign.direction > 0 ? track.length : 0),
        direction: sign.direction,
        speed,
      });
    }
  }
  return result;
}
export function speedAt(car, store) {
  if (!car?.direction) return null;
  let nearest = null;
  for (const sign of store.signs.values()) {
    if (
      sign.track !== car.track1 ||
      sign.direction !== car.direction ||
      sign.advance ||
      !sign.speeds?.length
    )
      continue;
    const gap = car.direction * ((car.span1 || 0) - sign.span);
    if (gap >= 0 && (!nearest || gap < nearest.gap))
      nearest = { gap, value: signSpeed(sign, store) };
  }
  return nearest?.value ?? null;
}
export function dashboard(store) {
  const occupied = new Set();
  for (const block of store.blocks.values())
    if (block.occupied) occupied.add([...block.tracks].sort().join("|"));
  return {
    trains: [...store.cars.values()].filter(isLocomotive).length,
    occupiedBlocks: occupied.size,
    stopSignals: [...store.signals.values()].filter(
      (s) => s.objectKind !== "sign" && s.stop && !s.off,
    ).length,
    activeOrders: [...store.jobs.values()].filter((j) => j.active).length,
    players: store.players.size,
    warnings: store.routes.filter((r) => r.warnings?.length).length,
  };
}
