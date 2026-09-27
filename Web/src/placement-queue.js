// One bounded geometry worker per map, with coalescing by live region identity.
// Existing positions remain usable while additions are placed. Stale results
// cannot replace a newer topology/size or resurrect a deleted signal.
export class PlacementQueue {
  constructor(changed, valid, WorkerType = globalThis.Worker) {
    this.changed = changed;
    this.valid = valid;
    this.pending = new Map();
    this.waiters = [];
    this.serial = 0;
    this.metrics = { submitted: 0, completed: 0, discarded: 0 };
    if (WorkerType)
      try {
        this.worker = new WorkerType(
          new URL("./signal-plan-worker.js", import.meta.url),
          { type: "module" },
        );
        this.worker.onmessage = ({ data }) => this.complete(data);
        this.worker.onerror = () => this.fail();
      } catch {
        this.worker = null;
      }
  }
  get busy() {
    return !!this.active || this.pending.size > 0 || !!this.preparing?.size;
  }
  submit(region, data) {
    if (!this.worker) return false;
    if (region.pending) return true;
    for (const old of this.pending.keys())
      if (!this.valid(old)) {
        this.pending.delete(old);
        old.pending = false;
        this.metrics.discarded++;
      }
    region.pending = true;
    this.pending.set(region, data);
    this.pump();
    return !!this.worker;
  }
  pump() {
    if (this.active || !this.worker) return;
    for (const [region, data] of this.pending) {
      this.pending.delete(region);
      if (!this.valid(region)) {
        region.pending = false;
        this.metrics.discarded++;
        continue;
      }
      const id = ++this.serial;
      this.active = { id, region };
      this.metrics.submitted++;
      try {
        this.worker.postMessage({ id, ...data });
      } catch {
        this.fail();
      }
      return;
    }
    if (!this.preparing?.size)
      for (const resolve of this.waiters.splice(0)) resolve();
  }
  complete(data) {
    const active = this.active;
    if (!active || active.id !== data.id) return;
    this.active = null;
    active.region.pending = false;
    if (data.error) {
      this.fail();
      return;
    }
    if (this.valid(active.region)) {
      active.region.plan = data.plan;
      active.region.positions = null;
      active.region.scale = null;
      active.region.entries = null;
      active.region.entryMap = null;
      active.region.prior = null;
      this.metrics.completed++;
      this.changed();
    } else this.metrics.discarded++;
    this.pump();
  }
  fail() {
    for (const work of this.preparing?.values() || []) work.return();
    this.preparing?.clear();
    this.worker?.terminate();
    this.worker = null;
    if (this.active) this.active.region.pending = false;
    this.active = null;
    for (const region of this.pending.keys()) region.pending = false;
    this.pending.clear();
    for (const resolve of this.waiters.splice(0)) resolve();
    this.changed();
  }
  idle() {
    return this.busy
      ? new Promise((resolve) => this.waiters.push(resolve))
      : Promise.resolve();
  }
  dispose() {
    this.changed = () => {};
    this.fail();
  }
}
