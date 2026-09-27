// Retain warning lifetimes only for the same actual ordered itinerary.
// A different variant, direction, endpoint or topology starts a new history.
export function previewWarnings(previous, next, now = Date.now()) {
  const signature = (r) =>
    JSON.stringify([
      r.from,
      r.to,
      r.train,
      r.via,
      r.tracks,
      r.directions,
      r.switches,
      r.turntables,
    ]);
  const before =
    previous && signature(previous) === signature(next) ? previous : null;
  const identity = (c) =>
    JSON.stringify([c.code, c.kind, c.target, c.track, c.train]);
  const active = new Map(
    (before?.conflicts || []).map((c) => [identity(c), c]),
  );
  const conflicts = [
    ...new Map((next.conflicts || []).map((c) => [identity(c), c])).values(),
  ].map((c) => ({
    ...c,
    firstSeen: active.get(identity(c))?.firstSeen || c.firstSeen || now,
  }));
  const present = new Set(conflicts.map(identity));
  const history = [...(before?.history || [])];
  for (const [key, c] of active)
    if (!present.has(key)) history.push({ ...c, resolvedAt: now });
  return { ...next, conflicts, history: history.slice(-200) };
}
