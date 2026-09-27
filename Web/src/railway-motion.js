const length = (t) => t?.length ?? t?.spans?.at(-1) ?? 0;

// A bounded walk over real directed endpoint links. Screen distance must never
// choose an adjacent line when a sample crossed short switch/DoubleTrack pieces.
export function nativeMotionPath(
  tracks,
  from,
  fromSpan,
  to,
  toSpan,
  limit = 250,
) {
  const first = tracks.get(from),
    last = tracks.get(to);
  if (!first || !last || !Number.isFinite(fromSpan) || !Number.isFinite(toSpan))
    return null;
  if (from === to) return [{ track: from, from: fromSpan, to: toSpan }];
  const queue = [0, 1].map((end) => ({
      id: from,
      end,
      cost: Math.abs((end ? length(first) : 0) - fromSpan),
      parts: [{ track: from, from: fromSpan, to: end ? length(first) : 0 }],
    })),
    seen = new Map();
  let visits = 0,
    best = null;
  while (queue.length && visits++ < 128) {
    queue.sort((a, b) => b.cost - a.cost);
    const current = queue.pop();
    if (current.cost > limit || (best && current.cost > best.cost)) continue;
    const track = tracks.get(current.id);
    for (const link of (current.end ? track.b : track.a) || []) {
      const next = tracks.get(link.track);
      if (!next || ![0, 1].includes(link.end)) continue;
      const start = link.end ? length(next) : 0,
        target = link.track === to ? toSpan : link.end ? 0 : length(next),
        cost = current.cost + Math.abs(target - start);
      if (cost > limit) continue;
      const parts = [
        ...current.parts,
        { track: link.track, from: start, to: target },
      ];
      if (link.track === to) {
        if (!best || cost < best.cost) best = { cost, parts };
        continue;
      }
      const key = link.track + ":" + link.end;
      if ((seen.get(key) ?? Infinity) <= cost) continue;
      seen.set(key, cost);
      queue.push({ id: link.track, end: 1 - link.end, cost, parts });
    }
  }
  return best?.parts || null;
}
export function sampleMotionPath(parts, alpha) {
  if (!parts?.length) return null;
  let at =
    parts.reduce((n, p) => n + Math.abs(p.to - p.from), 0) *
    Math.max(0, Math.min(1, alpha));
  for (const [i, p] of parts.entries()) {
    const d = Math.abs(p.to - p.from);
    if (at <= d || i === parts.length - 1)
      return {
        track: p.track,
        span: p.from + Math.sign(p.to - p.from) * Math.min(at, d),
        direction: Math.sign(p.to - p.from),
      };
    at -= d;
  }
}
