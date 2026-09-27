// Validate a revision before applying it, so reconnect never resumes past a
// partially applied snapshot/delta. The server remains the source of truth.
export function validateMessage(message, store) {
  const require = (condition) => {
    if (!condition) throw new Error("RESYNC_REQUIRED");
  };
  require(message && typeof message.type === "string");
  if (message.protocol !== undefined) require(message.protocol === 5);
  const p = message.payload;
  if (message.type === "receipt") {
    require(p && typeof p.id === "string");
    return;
  }
  if (message.type === "resumed") {
    require(
      message.serverEpoch === store.epoch && BigInt(message.seq) === store.seq,
    );
    return;
  }
  if (message.type === "snapshot" || message.type === "topology") {
    const topology = message.type === "snapshot" ? p?.topology : p;
    if (topology != null) {
      require(
        typeof topology.epoch === "string" &&
          Array.isArray(topology.tracks) &&
          Array.isArray(topology.junctions),
      );
      for (const t of topology.tracks)
        require(
          t &&
            typeof t.id === "string" &&
            Array.isArray(t.points) &&
            t.points.length >= 4 &&
            t.points.length % 2 === 0 &&
            t.points.every(Number.isFinite),
        );
      for (const j of topology.junctions)
        require(
          j &&
            typeof j.id === "string" &&
            Array.isArray(j.branches) &&
            Number.isFinite(j.x) &&
            Number.isFinite(j.z),
        );
      for (const table of topology.turntables || [])
        require(
          table &&
            typeof table.id === "string" &&
            typeof table.track === "string" &&
            Number.isFinite(table.x) &&
            Number.isFinite(table.z) &&
            Number.isFinite(table.radius) &&
            table.radius > 0 &&
            Array.isArray(table.ends) &&
            table.ends.every(
              (end) =>
                end &&
                typeof end.track === "string" &&
                [0, 1].includes(end.end) &&
                Number.isFinite(end.angle),
            ),
        );
    } else require(message.type === "snapshot");
  }
  if (message.type === "snapshot" || message.type === "delta") {
    require(p && typeof p === "object");
    for (const key of [...store.maps.filter((k) => k !== "tracks"), "motions"])
      if (p[key] != null) {
        require(Array.isArray(p[key]));
        for (const value of p[key])
          require(value && typeof value.id === "string" && value.id.length > 0);
      }
    for (const key of ["removedCars", "removedBlocks", "removedSignals"])
      if (p[key] != null)
        require(
          Array.isArray(p[key]) && p[key].every((id) => typeof id === "string"),
        );
    for (const value of p.cars || [])
      if (value.consistCargo != null)
        require(
          Number.isInteger(value.consistCargo.cars) &&
            value.consistCargo.cars >= 0 &&
            typeof value.consistCargo.complete === "boolean" &&
            Array.isArray(value.consistCargo.types) &&
            value.consistCargo.types.every(
              (v) => typeof v === "string" && v.length > 0,
            ) &&
            (!value.consistCargo.lengthKnown ||
              (Number.isFinite(value.consistCargo.length) &&
                value.consistCargo.length > 0)) &&
            (!value.consistCargo.membershipComplete ||
              (Array.isArray(value.consistCargo.members) &&
                value.consistCargo.members.length === value.consistCargo.cars &&
                value.consistCargo.members.every(
                  (id) => typeof id === "string" && id.length > 0,
                ) &&
                new Set(value.consistCargo.members).size ===
                  value.consistCargo.cars)),
        );
    for (const value of p.players || [])
      require(
        [value.x, value.z, value.yaw, value.sampledAt].every(Number.isFinite) &&
          (!value.carPoseKnown ||
            (typeof value.car === "string" &&
              [value.carX, value.carZ, value.carYaw].every(Number.isFinite))),
      );
    for (const value of p.signals || [])
      require(Array.isArray(value.lamps) && Array.isArray(value.aspects));
    for (const value of p.signals || [])
      if (value.routeBranches !== undefined)
        require(
          Array.isArray(value.routeBranches) &&
            value.routeBranches.length <= 64 &&
            value.routeBranches.every((id) => typeof id === "string"),
        );
    for (const value of p.signals || [])
      if (value.parts !== undefined)
        require(
          Array.isArray(value.parts) &&
            value.parts.length <= 64 &&
            value.parts.every(
              (part) =>
                part &&
                typeof part.id === "string" &&
                [part.x, part.y, part.worldX, part.worldZ].every(
                  Number.isFinite,
                ),
            ),
        );
    for (const value of p.signals || [])
      if (value.lampLayout !== undefined)
        require(
          Array.isArray(value.lampLayout) &&
            value.lampLayout.length <= 128 &&
            value.lampLayout.every(
              (l) =>
                l &&
                typeof l.id === "string" &&
                Number.isFinite(l.x) &&
                Number.isFinite(l.y),
            ),
        );
    for (const value of p.locations || [])
      require(
        Number.isFinite(value.x) &&
          Number.isFinite(value.z) &&
          Array.isArray(value.tracks) &&
          value.tracks.every((id) => typeof id === "string"),
      );
    for (const value of p.turntables || [])
      require(
        Array.isArray(value.points) &&
          value.points.length === 4 &&
          value.points.every(Number.isFinite) &&
          Number.isFinite(value.angle) &&
          Number.isFinite(value.target),
      );
    for (const value of p.blocks || [])
      require(
        Array.isArray(value.tracks) &&
          Array.isArray(value.extraTracks) &&
          Array.isArray(value.trains),
      );
    if (p.motions?.length) {
      const added = new Set((p.cars || []).map((c) => c.id));
      require(
        p.motions.every(
          (m) => added.has(m.id) || (!p.reset && store.cars.has(m.id)),
        ),
      );
    }
  }
  if (message.type === "routes") require(Array.isArray(p));
  if (message.type === "event" || message.type === "capabilities")
    require(p && typeof p === "object");
}
