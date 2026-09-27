export class Network extends EventTarget {
  constructor(store) {
    super();
    this.store = store;
    this.socket = null;
    this.retry = 0;
    this.timer = 0;
    this.enabled = false;
    this.bytes = 0;
    this.waiters = new Map();
    this.user = null;
    this.status = "offline";
    this.authRevision = 0;
  }
  signal(status) {
    this.status = status;
    this.store.disconnected = status !== "connected";
    this.store.emit("connection");
    this.dispatchEvent(new CustomEvent("status", { detail: status }));
  }
  async me() {
    const r = await fetch("/api/me", { cache: "no-store" });
    this.user = r.ok ? await r.json() : null;
    return this.user;
  }
  async login(body) {
    this.authRevision++;
    this.healthRequest?.controller.abort();
    const r = await fetch("/api/login", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body),
    });
    if (!r.ok)
      throw new Error(
        r.status === 429
          ? "RATE_LIMITED"
          : r.status === 401
            ? "AUTH_REQUIRED"
            : "SERVER_UNAVAILABLE",
      );
    this.user = await r.json();
    this.enabled = true;
    this.connect();
    return this.user;
  }
  start() {
    this.enabled = true;
    this.connect();
  }
  connect() {
    clearTimeout(this.timer);
    if (!this.enabled || (this.socket && this.socket.readyState < 2)) return;
    this.signal(this.retry ? "reconnecting" : "connecting");
    const url = new URL("/api/ws", location);
    url.protocol = location.protocol === "https:" ? "wss:" : "ws:";
    url.searchParams.set("epoch", this.store.epoch);
    url.searchParams.set("seq", this.store.seq.toString());
    const socket = new WebSocket(url);
    this.socket = socket;
    socket.onopen = () => {
      if (socket !== this.socket) return;
      this.retry = 0;
      this.signal("connected");
    };
    socket.binaryType = "arraybuffer";
    let chain = Promise.resolve(),
      queued = 0,
      queuedBytes = 0,
      valid = true;
    const resync = () => {
      if (!valid || socket !== this.socket) return;
      valid = false;
      this.store.epoch = "";
      this.store.seq = 0n;
      this.store.disconnected = true;
      this.store.emit("connection");
      socket.close();
    };
    socket.onmessage = (e) => {
      if (socket !== this.socket || !valid) return;
      const size =
        typeof e.data === "string" ? e.data.length * 2 : e.data.byteLength;
      this.bytes += size;
      queuedBytes += size;
      // The Host may replay 48 revisions plus its resumed marker. Bound both
      // count and retained bytes to its 64-frame / 24 MiB peer budget.
      if (++queued > 64 || queuedBytes > 24 * 1024 * 1024) {
        queued--;
        resync();
        return;
      }
      chain = chain.then(async () => {
        try {
          if (socket !== this.socket || !valid) return;
          const text =
            typeof e.data === "string"
              ? e.data
              : await new Response(
                  new Blob([e.data])
                    .stream()
                    .pipeThrough(new DecompressionStream("gzip")),
                ).text();
          if (socket !== this.socket || !valid) return;
          const message = JSON.parse(text);
          this.store.consume(message);
          if (
            message.type === "receipt" &&
            message.payload.status !== "accepted"
          ) {
            const w = this.waiters.get(message.payload.id);
            if (w) {
              clearTimeout(w.timer);
              this.waiters.delete(message.payload.id);
              w.resolve(message.payload);
            }
          }
        } catch (err) {
          console.warn("ADS_RESYNC", err.name);
          resync();
        } finally {
          queued--;
          queuedBytes -= size;
        }
      });
    };
    socket.onclose = () => {
      if (socket !== this.socket) return;
      this.socket = null;
      this.signal("offline");
      this.clearWaiters();
      if (this.enabled) {
        const delay =
          Math.min(15000, 500 * 2 ** Math.min(5, this.retry++)) +
          Math.random() * 300;
        this.timer = setTimeout(() => this.connect(), delay);
      }
    };
    socket.onerror = () => socket.close();
  }
  clearWaiters() {
    for (const [id, w] of this.waiters) {
      clearTimeout(w.timer);
      w.resolve({ id, status: "outcomeUnknown", code: "GAME_DISCONNECTED" });
    }
    this.waiters.clear();
  }
  stop() {
    this.healthRequest?.controller.abort();
    this.enabled = false;
    clearTimeout(this.timer);
    const socket = this.socket;
    this.socket = null;
    socket?.close();
    this.clearWaiters();
    this.signal("offline");
  }
  async logout() {
    this.authRevision++;
    this.stop();
    this.user = null;
    const r = await fetch("/api/logout", { method: "POST" });
    if (!r.ok) throw new Error("LOGOUT_FAILED");
  }
  async accounts() {
    const r = await fetch("/api/accounts", { cache: "no-store" });
    if (!r.ok)
      throw new Error(r.status === 401 ? "AUTH_REQUIRED" : "FORBIDDEN");
    return r.json();
  }
  command(kind, fields = {}) {
    const id = crypto.randomUUID
      ? crypto.randomUUID()
      : [...crypto.getRandomValues(new Uint8Array(16))]
          .map((x) => x.toString(16).padStart(2, "0"))
          .join("");
    if (this.socket?.readyState !== WebSocket.OPEN)
      return Promise.resolve({
        id,
        status: "rejected",
        code: "GAME_DISCONNECTED",
      });
    if (!this.store.canControl || !this.user || this.user.role === "viewer")
      return Promise.resolve({ id, status: "rejected", code: "FORBIDDEN" });
    if (
      kind === "loco" &&
      (this.user.role !== "admin" || !this.store.capabilities.locoControls)
    )
      return Promise.resolve({ id, status: "rejected", code: "FORBIDDEN" });
    const payload = {
      id,
      kind,
      epoch: this.store.topology.epoch,
      topologyRevision: this.store.topology.revision,
      ...fields,
    };
    if (this.waiters.size >= 64)
      return Promise.resolve({ id, status: "rejected", code: "QUEUE_FULL" });
    return new Promise((resolve) => {
      const timer = setTimeout(
        () => {
          this.waiters.delete(id);
          resolve({ id, status: "outcomeUnknown", code: "COMMAND_TIMEOUT" });
        },
        [
          "planRoute",
          "applyRoute",
          "reserveRoute",
          "reserveRouteSignals",
          "releaseRouteSignals",
        ].includes(kind)
          ? 240000
          : kind === "setTurntable"
            ? 50000
            : 22000,
      );
      this.waiters.set(id, { resolve, timer });
      try {
        this.socket.send(JSON.stringify(payload));
      } catch (error) {
        console.warn("ADS_COMMAND_SEND_FAILED", error.name);
        clearTimeout(timer);
        this.waiters.delete(id);
        resolve({ id, status: "outcomeUnknown", code: "GAME_DISCONNECTED" });
      }
    });
  }
  async route(from, to, train = "", signal, via = []) {
    const query = new URLSearchParams({ from, to, train });
    if (via?.length) query.set("via", via.join(","));
    const r = await fetch(
      "/api/route?" + query,
      { signal },
    );
    if (!r.ok) {
      const detail = r.headers.get("content-type")?.includes("json")
        ? await r.json()
        : null;
      throw new Error(
        detail?.code ||
          (r.status === 401
            ? "AUTH_REQUIRED"
            : r.status === 429
              ? "RATE_LIMITED"
              : "NO_ROUTE"),
      );
    }
    return r.json();
  }
  health() {
    const prior = this.healthRequest;
    if (
      prior &&
      prior.user === this.user &&
      prior.revision === this.authRevision &&
      !prior.controller.signal.aborted
    )
      return prior.promise;
    prior?.controller.abort();
    const request = {
      user: this.user,
      revision: this.authRevision,
      controller: new globalThis.AbortController(),
    };
    const current = () =>
      !request.controller.signal.aborted &&
      request.user === this.user &&
      request.revision === this.authRevision;
    this.healthRequest = request;
    const timer = setTimeout(() => request.controller.abort(), 5000);
    request.promise = (async () => {
      try {
        const r = await fetch("/api/health", {
          cache: "no-store",
          signal: request.controller.signal,
        });
        if (!current()) return null;
        if (r.status === 401) {
          this.stop();
          this.user = null;
          this.dispatchEvent(new Event("auth-expired"));
          return null;
        }
        const result = r.ok ? await r.json() : null;
        return current() ? result : null;
      } finally {
        clearTimeout(timer);
        if (this.healthRequest === request) this.healthRequest = null;
      }
    })();
    return request.promise;
  }
}
