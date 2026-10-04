const mixAngle = (a, b, u) => a + (((b - a + 540) % 360) - 180) * u;

export function recordPlayers(store, players, replace = false) {
  const history = store.playerHistory;
  if (replace) {
    const live = new Set(players.map((p) => p.id));
    for (const id of history.keys()) if (!live.has(id)) history.delete(id);
    store.players.clear();
  }
  for (const player of players) {
    if (
      ![player.x, player.z, player.yaw, player.sampledAt].every(Number.isFinite)
    )
      continue;
    let samples = history.get(player.id) || [];
    const last = samples.at(-1);
    if (last && player.sampledAt < last.sampledAt) {
      store.players.set(player.id, last);
      continue;
    }
    if (
      last &&
      (player.sampledAt - last.sampledAt > 2000 ||
        Math.hypot(player.x - last.x, player.z - last.z) > 100)
    )
      samples = [];
    if (samples.at(-1)?.sampledAt === player.sampledAt) samples.pop();
    samples.push({ ...player });
    if (samples.length > 16) samples.shift();
    history.set(player.id, samples);
    store.players.set(player.id, player);
  }
}

export function playerPosition(store, player, now) {
  const samples = store.playerHistory.get(player.id);
  if (!samples?.length) return player;
  let i = 1;
  while (i < samples.length && samples[i].sampledAt < now) i++;
  const a =
      now < samples[0].sampledAt
        ? samples[0]
        : samples[Math.min(i - 1, samples.length - 1)],
    b =
      now < samples[0].sampledAt ? a : samples[Math.min(i, samples.length - 1)],
    u =
      a === b
        ? 0
        : Math.max(
            0,
            Math.min(1, (now - a.sampledAt) / (b.sampledAt - a.sampledAt)),
          );
  const car =
    a.car &&
    a.car === b.car &&
    a.carPoseKnown &&
    b.carPoseKnown &&
    now - b.sampledAt <= 2000 &&
    store.cars.get(a.car);
  if (
    car &&
    [a.carX, a.carZ, b.carX, b.carZ, a.yaw, b.yaw].every(Number.isFinite)
  ) {
    const pose = store.position(car, now),
      angle = (pose.yaw * Math.PI) / 180,
      x = a.carX + (b.carX - a.carX) * u,
      z = a.carZ + (b.carZ - a.carZ) * u;
    return {
      x: pose.x + x * Math.cos(angle) + z * Math.sin(angle),
      z: pose.z - x * Math.sin(angle) + z * Math.cos(angle),
      // `a.yaw`/`b.yaw` are the authoritative world-facing player heading.
      // Rebuilding it as car heading + relative heading makes the two
      // independently interpolated angles choose different 180° branches,
      // which visibly turns an onboard marker sideways during reversals and
      // multiplayer updates. The car pose is still used for the position;
      // the arrow orientation follows the player's actual world rotation.
      yaw: mixAngle(a.yaw, b.yaw, u),
    };
  }
  // Boarding, leaving and crossing between vehicles interpolate actual world
  // samples; there is no proximity attachment or accumulated smoothing error.
  return {
    x: a.x + (b.x - a.x) * u,
    z: a.z + (b.z - a.z) * u,
    yaw: mixAngle(a.yaw, b.yaw, u),
  };
}
