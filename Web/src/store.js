import { isLocomotive, isWagon } from "./rolling-stock.js";
import { consistLoadRating } from "./locomotive-catalog.js";
import { nativeMotionPath, sampleMotionPath } from "./railway-motion.js";
import { recordPlayers, playerPosition } from "./player-motion.js";
import { validateMessage } from "./state-validation.js";
import { applyTurntables } from "./turntables.js";
import { wagonGroup } from "./wagon-groups.js";
export class Store extends EventTarget {
  constructor() {
    super();
    this.epoch = "";
    this.seq = 0n;
    this.jobRevision = 0;
    this.signalVersions = new Map();
    this.nextSignalVersion = 0;
    this.topology = null;
    this.capabilities = { status: "loading" };
    this.routes = [];
    this.events = [];
    this.preview = null;
    this.serverOffset = 0;
    this.lastMessage = 0;
    this.maps = [
      "tracks",
      "switches",
      "turntables",
      "cars",
      "signals",
      "locations",
      "blocks",
      "occupancy",
      "players",
      "jobs",
      "signs",
    ];
    for (const key of this.maps) this[key] = new Map();
    this.junctions = new Map();
    this.tableDefs = new Map();
    this.tableTracks = new Set();
    this.consists = new Map();
    this.wagonGroupCache = new Map();
    this.motionHistory = new Map();
    this.motionPaths = new WeakMap();
    this.playerHistory = new Map();
    this.motionDelay = 200;
  }
  emit(kind, payload) {
    if (["snapshot", "topology"].includes(kind) || payload?.reset) {
      this.signalVersions.clear();
      for (const id of this.signals.keys())
        this.signalVersions.set(id, ++this.nextSignalVersion);
    }
    for (const signal of payload?.signals || [])
      this.signalVersions.set(signal.id, ++this.nextSignalVersion);
    for (const id of payload?.removedSignals || [])
      this.signalVersions.delete(id);
    if (
      ["snapshot", "topology"].includes(kind) ||
      payload?.signs?.length ||
      payload?.replaceSigns
    )
      this.signGeometryRevision = (this.signGeometryRevision || 0) + 1;
    if (
      ["snapshot", "topology"].includes(kind) ||
      payload?.signals?.length ||
      payload?.blocks?.length ||
      payload?.removedBlocks?.length ||
      payload?.removedSignals?.length ||
      payload?.signs?.length ||
      payload?.replaceSigns ||
      payload?.reset
    )
      this.presentationRevision = (this.presentationRevision || 0) + 1;
    this.dispatchEvent(
      new CustomEvent("change", { detail: { kind, payload } }),
    );
  }
  topologySet(topology) {
    this.capabilities = {
      ...this.capabilities,
      status: "loading",
      authority: false,
    };
    this.topology = topology;
    this.motionPaths = new WeakMap();
    this.tracks = new Map((topology?.tracks || []).map((x) => [x.id, x]));
    this.junctions = new Map((topology?.junctions || []).map((x) => [x.id, x]));
    this.tableDefs = new Map(
      (topology?.turntables || []).map((x) => [x.id, x]),
    );
    this.tableTracks = new Set(
      [...this.tableDefs.values()].map((x) => x.track),
    );
    this.preview = null;
  }
  clearState(plans = true) {
    this.jobsKnown = false;
    this.jobRevision++;
    for (const key of this.maps) if (key !== "tracks") this[key].clear();
    this.consists.clear();
    this.wagonGroupCache.clear();
    this.motionHistory.clear();
    this.playerHistory.clear();
    this.motionDelay = 200;
    this.renderTimestamp = this.renderWallTime = null;
    if (plans) {
      this.routes = [];
      this.events = [];
    }
  }
  setCar(car) {
    const old = this.cars.get(car.id);
    // Metadata and compact motion travel in separate ordered frames. An older
    // motion from the same capture batch must not undo a newer full car state.
    if (old && car.sampledAt < old.sampledAt) return;
    if (
      !old ||
      old.consist !== car.consist ||
      old.nativeTrainset !== car.nativeTrainset ||
      old.vehicleCategory !== car.vehicleCategory ||
      old.locomotive !== car.locomotive ||
      old.length !== car.length ||
      old.mass !== car.mass ||
      old.massKnown !== car.massKnown ||
      old.order !== car.order
    )
      this.wagonGroupCache.delete(car.consist);
    if (old) {
      if (old.consist !== car.consist) this.removeMember(old);
    }
    if (
      Number.isFinite(car.sampledAt) &&
      Number.isFinite(car.x) &&
      Number.isFinite(car.z)
    ) {
      let samples = this.motionHistory.get(car.id);
      const gap = old ? car.sampledAt - old.sampledAt : 0;
      // Teleports/re-rails and a long disconnect start a new trajectory.
      const teleport =
        old &&
        Number.isFinite(car.speed) &&
        Number.isFinite(old.speed) &&
        Math.hypot(car.x - old.x, car.z - old.z) >
          20 +
            (((Math.max(Math.abs(car.speed), Math.abs(old.speed)) / 3.6) *
              gap) /
              1000) *
              3;
      if (!samples || gap > 2000 || teleport) samples = [];
      if (samples.at(-1)?.sampledAt === car.sampledAt) samples.pop();
      samples.push({
        x: car.x,
        z: car.z,
        yaw: car.yaw,
        sampledAt: car.sampledAt,
        track1: car.track1,
        track2: car.track2,
        span1: car.span1,
        span2: car.span2,
        derailed: car.derailed,
      });
      if (samples.length > 16) samples.shift();
      this.motionHistory.set(car.id, samples);
      if (gap > 0 && gap < 1500 && !teleport && Math.abs(old?.speed || 0) > 0.1)
        this.batchMotionGap = Math.max(this.batchMotionGap || 0, gap);
    }
    this.cars.set(car.id, car);
    if (!this.consists.has(car.consist))
      this.consists.set(car.consist, new Set());
    this.consists.get(car.consist).add(car.id);
  }
  removeMember(car) {
    this.wagonGroupCache.delete(car.consist);
    const group = this.consists.get(car.consist);
    group?.delete(car.id);
    if (group?.size === 0) this.consists.delete(car.consist);
  }
  consume(message) {
    validateMessage(message, this);
    if (message.type === "receipt") {
      this.emit("receipt", message.payload);
      return;
    }
    if (message.time)
      this.serverOffset = this.lastMessage
        ? this.serverOffset * 0.9 + (message.time - Date.now()) * 0.1
        : message.time - Date.now();
    this.lastMessage = performance.now();
    if (message.type === "snapshot") {
      this.epoch = message.serverEpoch;
      this.seq = BigInt(message.seq);
      this.clearState();
      const p = message.payload;
      this.jobsKnown = Array.isArray(p.jobs);
      this.batchMotionGap = 0;
      this.topologySet(p.topology);
      for (const key of this.maps)
        if (key !== "tracks" && key !== "cars" && key !== "players")
          for (const value of p[key] || []) this[key].set(value.id, value);
      for (const car of p.cars || []) this.setCar(car);
      recordPlayers(this, p.players || [], !!p.replacePlayers);
      applyTurntables(this, p.turntables);
      this.routes = p.routes || [];
      this.events = p.events || [];
      this.capabilities = p.capabilities || { status: "loading" };
      this.emit("snapshot", p);
      return;
    }
    if (message.type === "resumed") {
      this.emit("resumed");
      return;
    }
    if (
      message.serverEpoch !== this.epoch ||
      BigInt(message.seq) !== this.seq + 1n
    )
      throw new Error("RESYNC_REQUIRED");
    if (
      message.type === "delta" &&
      (!this.topology ||
        message.payload.epoch !== this.topology.epoch ||
        message.payload.topologyRevision !== this.topology.revision)
    )
      throw new Error("RESYNC_REQUIRED");
    this.seq = BigInt(message.seq);
    if (message.type === "topology") {
      this.clearState(false);
      this.topologySet(message.payload);
      this.emit("topology", message.payload);
      return;
    }
    if (message.type === "delta") {
      const p = message.payload;
      this.batchMotionGap = 0;
      if (
        p.epoch !== this.topology?.epoch ||
        p.topologyRevision !== this.topology.revision
      )
        throw new Error("RESYNC_REQUIRED");
      if (p.reset) this.clearState(false);

      if (p.replaceJobs || p.jobs?.length) this.jobRevision++;
      if (p.replaceJobs) this.jobsKnown = true;
      if (p.replaceJobs) this.jobs.clear();
      if (p.replaceLocations) this.locations.clear();
      if (p.replaceSigns) this.signs.clear();
      for (const key of this.maps)
        if (key !== "tracks" && key !== "cars" && key !== "players")
          for (const value of p[key] || []) this[key].set(value.id, value);
      for (const car of p.cars || []) this.setCar(car);
      for (const motion of p.motions || []) {
        const car = this.cars.get(motion.id);
        if (!car) throw new Error("RESYNC_REQUIRED");
        this.setCar({ ...car, ...motion });
      }
      for (const id of p.removedCars || []) {
        const car = this.cars.get(id);
        if (car) this.removeMember(car);
        this.cars.delete(id);
        this.motionHistory.delete(id);
      }
      for (const id of p.removedSignals || []) this.signals.delete(id);
      for (const id of p.removedBlocks || []) this.blocks.delete(id);
      if (p.capabilities) this.capabilities = p.capabilities;
      recordPlayers(this, p.players || [], !!p.replacePlayers);
      applyTurntables(this, p.turntables);
      if (this.batchMotionGap) {
        const desiredDelay = Math.min(
          1000,
          Math.max(160, this.batchMotionGap * 1.6 + 60),
        );
        this.motionDelay = Math.max(desiredDelay, this.motionDelay * 0.995);
      }
      this.emit("delta", p);
    } else if (message.type === "routes") {
      this.routes = message.payload;
      this.emit("routes");
    } else if (message.type === "event") {
      if (this.events.some((e) => e.id === message.payload.id)) return;
      this.events.push(message.payload);
      if (this.events.length > 1000) this.events.shift();
      this.emit("event", message.payload);
    } else if (message.type === "capabilities") {
      this.capabilities = message.payload;
      this.emit("capabilities");
    }
  }
  get stale() {
    return (
      this.disconnected ||
      !this.lastMessage ||
      performance.now() - this.lastMessage > 5000 ||
      this.capabilities.status !== "ready"
    );
  }
  get canControl() {
    return !this.stale && this.capabilities.authority;
  }
  train(id) {
    const ids = this.consists.get(id);
    if (!ids) return null;
    const cars = [...ids]
      .map((x) => this.cars.get(x))
      .filter(Boolean)
      .sort((a, b) => a.order - b.order);
    if (!cars.length) return null;
    const locomotives = cars.filter(isLocomotive);
    const head = locomotives[0] || cars[0];
    const lengthsKnown = cars.every(
      (c) => Number.isFinite(Number(c.length)) && Number(c.length) > 0,
    );
    const massesKnown = cars.every(
      (c) =>
        c.massKnown === true &&
        Number.isFinite(Number(c.mass)) &&
        Number(c.mass) >= 0,
    );
    const tractionValuesKnown =
      locomotives.length > 0 &&
      locomotives.every(
        (c) =>
          c.tractionKnown === true &&
          Number.isFinite(Number(c.availableTraction)) &&
          Number(c.availableTraction) >= 0,
      );
    return {
      ...head,
      id,
      name: head.name,
      kind: "trains",
      count: cars.length,
      locomotiveCount: locomotives.length,
      nativeTrainset: cars.some((c) => typeof c.nativeTrainset === "boolean")
        ? cars.some((c) => c.nativeTrainset)
        : undefined,
      wagonCount: cars.filter(isWagon).length,
      length: lengthsKnown
        ? cars.reduce((n, c) => n + Number(c.length), 0)
        : null,
      massKnown: massesKnown,
      mass: massesKnown
        ? cars.reduce((n, c) => n + (Number(c.mass) || 0), 0)
        : 0,
      consistMass: massesKnown
        ? cars.reduce((n, c) => n + (Number(c.mass) || 0), 0)
        : 0,
      tractionKnown: tractionValuesKnown,
      availableTraction: tractionValuesKnown
        ? locomotives.reduce((n, c) => n + (Number(c.availableTraction) || 0), 0)
        : 0,
      // Catalogue values are only a conservative load-rating reference.  Add
      // one entry per native locomotive so a multi-locomotive consist does not
      // incorrectly inherit the lead unit's single-unit rating.
      tractionRating: consistLoadRating(locomotives),
      carIds: cars.map((x) => x.id),
      cargo: [
        ...new Set(
          cars
            .filter(
              (c) => c.cargoKnown && c.cargoAmount > 0 && c.cargo !== "None",
            )
            .map((c) => c.cargo),
        ),
      ],
      head: head.id,
    };
  }
  wagonGroup(id) {
    return wagonGroup(this, id);
  }
  playerPosition(player, now = this.presentationTime()) {
    return playerPosition(this, player, now);
  }
  presentationTime(wall = performance.now()) {
    const desired = Date.now() + this.serverOffset - this.motionDelay;
    const elapsed =
      this.renderWallTime == null ? 0 : Math.max(0, wall - this.renderWallTime);
    if (this.renderTimestamp == null || elapsed > 2000)
      this.renderTimestamp = desired;
    else
      this.renderTimestamp += Math.min(
        elapsed * 1.1,
        Math.max(elapsed * 0.9, desired - this.renderTimestamp),
      );
    this.renderWallTime = Math.max(wall, this.renderWallTime ?? wall);
    return this.renderTimestamp;
  }
  position(car, now = this.presentationTime(), target = null) {
    const samples = this.motionHistory.get(car.head || car.id);
    if (!samples?.length) return car;
    if (now <= samples[0].sampledAt) return samples[0];
    let at = 1;
    while (at < samples.length && samples[at].sampledAt < now) at++;
    if (at === samples.length) return samples.at(-1);
    const p = samples[at - 1],
      next = samples[at];
    const alpha = Math.max(
      0,
      Math.min(1, (now - p.sampledAt) / (next.sampledAt - p.sampledAt)),
    );
    const dy = ((next.yaw - p.yaw + 540) % 360) - 180;
    const result = target || {};
    result.x = p.x + (next.x - p.x) * alpha;
    result.z = p.z + (next.z - p.z) * alpha;
    result.yaw = p.yaw + dy * alpha;
    // Bogie ownership must use the same time sample as the interpolated pose.
    for (const i of [1, 2]) {
      const key = "track" + i,
        span = "span" + i;
      if (p[key] === next[key]) {
        result[key] = p[key];
        result[span] = p[span] + (next[span] - p[span]) * alpha;
      } else {
        let cache = this.motionPaths.get(next);
        if (!cache || cache.previous !== p) {
          cache = { previous: p };
          this.motionPaths.set(next, cache);
        }
        if (!(key in cache))
          cache[key] = nativeMotionPath(
            this.tracks,
            p[key],
            p[span],
            next[key],
            next[span],
            Math.max(60, Math.hypot(next.x - p.x, next.z - p.z) * 3 + 30),
          );
        const sample = sampleMotionPath(cache[key], alpha),
          known = alpha < 1 ? p : next;
        result[key] = sample?.track ?? known[key];
        result[span] = sample?.span ?? known[span];
      }
    }
    result.derailed = alpha < 0.5 ? p.derailed : next.derailed;
    return result;
  }
}
