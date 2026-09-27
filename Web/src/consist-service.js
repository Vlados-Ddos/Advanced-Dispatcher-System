// A job belongs to this physical consist only through its native car GUIDs.
// Cargo, location, owner name and job-number prefixes are not matching keys.
const indexes = new WeakMap();
export function consistService(store, car) {
  const summary = car?.consistCargo;
  const members = summary?.membershipComplete
    ? summary.members
    : [...(store.consists.get(car?.consist) || [car?.id])];
  let index = indexes.get(store);
  if (
    !index ||
    index.revision !== store.jobRevision ||
    index.source !== store.jobs
  ) {
    index = {
      revision: store.jobRevision,
      source: store.jobs,
      byCar: new Map(),
      staleCars: new Set(),
    };
    for (const job of store.jobs.values()) {
      if (job.dataQuality === "stale") {
        for (const id of job.cars || []) index.staleCars.add(id);
        continue;
      }
      // Terminal state wins over an inconsistent legacy active flag.
      if (job.state !== "InProgress" && !(job.active && !job.state)) continue;
      for (const id of job.cars || []) {
        if (!index.byCar.has(id)) index.byCar.set(id, []);
        index.byCar.get(id).push(job);
      }
    }
    indexes.set(store, index);
  }
  const jobs = new Map();
  for (const id of members || [])
    for (const job of index.byCar.get(id) || []) jobs.set(job.id, job);
  return {
    jobs: [...jobs.values()].sort((a, b) => a.id.localeCompare(b.id)),
    complete:
      store.jobsKnown === true &&
      summary?.membershipComplete === true &&
      !members.some((id) => index.staleCars.has(id)),
    length:
      summary?.lengthKnown &&
      Number.isFinite(summary.length) &&
      summary.length > 0
        ? summary.length
        : null,
  };
}
