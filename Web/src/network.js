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
    this.lastError = "";
    this.exhausted = false;
    this.connectionRevision = 0;
    this.activityRevision = 0;
    this.reportedActivity = 0;
  }
  async request(url, options = {}, read = r => r.json(), timeout = 8000) {
    const controller = new AbortController();
    const external = options.signal;
    let timer, cancel;
    const cancelled = new Promise((_, reject) => {
      cancel = () => { controller.abort(); reject(new globalThis.DOMException("Aborted", "AbortError")); };
      external?.addEventListener("abort", cancel, { once: true });
      if (external?.aborted) cancel();
      timer = setTimeout(() => {
        controller.abort();
        reject(new Error("HOST_TIMEOUT"));
      }, timeout);
    });
    try {
      // The deadline includes decoding the entire body, even if a transport
      // implementation never rejects its pending operation when aborted.
      return await Promise.race([cancelled, (async () => {
        let response;
        try { response = await fetch(url, { cache: "no-store", ...options, signal: controller.signal }); }
        catch (error) {
          if (controller.signal.aborted) throw error;
          throw new Error("HOST_UNAVAILABLE");
        }
        if (controller.signal.aborted) throw new globalThis.DOMException("Aborted", "AbortError");
        try { return await read(response); }
        catch (error) {
          if (error instanceof SyntaxError) throw new Error("HOST_BAD_RESPONSE");
          if (error instanceof TypeError) throw new Error("HOST_UNAVAILABLE");
          throw error;
        }
      })()]);
    } finally {
      clearTimeout(timer);
      external?.removeEventListener("abort", cancel);
    }
  }
  async emptyBody(response) {
    if (response.status !== 204 && typeof response.text === "function") await response.text();
  }
  signal(status) {
    this.status = status;
    this.store.disconnected = status !== "connected";
    this.store.emit("connection");
    this.dispatchEvent(new CustomEvent("status", { detail: status }));
  }
  noteActivity() {
    if (this.user) this.activityRevision++;
  }
  async me() {
    const revision = this.authRevision;
    const user = await this.request("/api/me", {}, async r => {
      if (r.status === 401) return null;
      if (!r.ok) throw new Error("HOST_UNAVAILABLE");
      return r.json();
    });
    if (revision !== this.authRevision) return this.user;
    this.user = user;
    return this.user;
  }
  async login(body) {
    const revision = ++this.authRevision;
    this.healthRequest?.controller.abort();
    // A prior logout response deletes its cookie. Let it finish before a new
    // login can set a replacement cookie in this browser.
    if (this.logoutRequest) await this.logoutRequest.catch(() => {});
    if (revision !== this.authRevision) throw new Error("AUTH_STALE");
    const user = await this.request("/api/login", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
      }, async r => {
    // Logout (or a newer login) can invalidate this request while the
    // response is in flight.  Do not let a stale response restore a user or
    // reopen the WebSocket after the newer authentication state has won.
    if (revision !== this.authRevision) throw new Error("AUTH_STALE");
    if (!r.ok)
      throw new Error(
        r.status === 429
          ? "RATE_LIMITED"
          : r.status === 409
            ? "SESSION_ACTIVE"
          : r.status === 401
            ? "AUTH_REQUIRED"
            : r.status === 426
              ? "INSECURE_TRANSPORT"
              : "SERVER_UNAVAILABLE",
      );
    return r.json();
    }, 10000);
    if (revision !== this.authRevision) throw new Error("AUTH_STALE");
    this.user = user;
    this.enabled = true;
    this.retry = 0;
    this.exhausted = false;
    this.connect();
    return this.user;
  }
  start() {
    if (this.exhausted) { this.signal("reconnectFailed"); return; }
    this.enabled = true;
    this.connect();
  }
  retryConnection() {
    if (this.retryRequest) return this.retryRequest.promise;
    this.stop();
    this.enabled = true;
    this.signal("checkingSession");
    const request = { revision: this.authRevision, connection: this.connectionRevision };
    this.retryRequest = request;
    const current = () => this.authRevision === request.revision && this.connectionRevision === request.connection;
    request.promise = (async () => {
      try {
        const user = await this.me();
        if (!current()) return false;
        if (!user) {
          this.authRevision++;
          this.stop();
          this.dispatchEvent(new Event("auth-expired"));
          return false;
        }
        this.retry = 0;
        this.exhausted = false;
        this.lastError = "";
        this.connect();
        return true;
      } catch (error) {
        if (!current()) return false;
        this.exhausted = true;
        this.enabled = false;
        this.lastError = ["HOST_TIMEOUT", "HOST_BAD_RESPONSE"].includes(error.message) ? error.message : "HOST_UNAVAILABLE";
        this.signal("reconnectFailed");
        return false;
      } finally {
        if (this.retryRequest === request) this.retryRequest = null;
      }
    })();
    return request.promise;
  }
  connect() {
    clearTimeout(this.timer);
    if (!this.enabled || this.exhausted || (this.socket && this.socket.readyState < 2)) return;
    this.signal(this.retry ? "reconnecting" : "connecting");
    const url = new URL("/api/ws", location);
    url.protocol = location.protocol === "https:" ? "wss:" : "ws:";
    url.searchParams.set("epoch", this.store.epoch);
    url.searchParams.set("seq", this.store.seq.toString());
    let socket;
    try { socket = new WebSocket(url); }
    catch { this.lastError = "HOST_UNAVAILABLE"; this.reconnect(); return; }
    this.socket = socket;
    let stateTimer;
    const cleanup = () => { clearTimeout(openTimer); clearTimeout(stateTimer); valid = false; };
    const disconnect = (error = "HOST_UNAVAILABLE") => {
      if (socket !== this.socket) return;
      cleanup();
      this.socket = null;
      this.socketCleanup = null;
      this.disconnectSocket = null;
      this.lastError = error;
      this.clearWaiters();
      this.reconnect();
      try { socket.close(); } catch { /* Detached sockets cannot affect the replacement. */ }
    };
    const openTimer = setTimeout(() => {
      disconnect("HOST_TIMEOUT");
    }, 8000);
    this.socketCleanup = cleanup;
    this.disconnectSocket = disconnect;
    socket.onopen = () => {
      if (socket !== this.socket) return;
      clearTimeout(openTimer);
      this.signal("synchronizing");
      stateTimer = setTimeout(() => disconnect("HOST_STATE_TIMEOUT"), 10000);
    };
    socket.binaryType = "arraybuffer";
    let chain = Promise.resolve(),
      queued = 0,
      queuedBytes = 0,
      valid = true;
    const resync = () => {
      if (!valid || socket !== this.socket) return;
      this.store.epoch = "";
      this.store.seq = 0n;
      this.store.disconnected = true;
      this.store.emit("connection");
      disconnect("HOST_BAD_RESPONSE");
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
          if (message.type === "snapshot" || message.type === "resumed") {
            clearTimeout(stateTimer);
            this.retry = 0;
            this.exhausted = false;
            this.lastError = "";
            this.signal("connected");
          }
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
    socket.onclose = () => disconnect();
    socket.onerror = () => disconnect();
  }
  reconnect() {
    clearTimeout(this.timer);
    if (!this.enabled) { this.signal(this.exhausted ? "reconnectFailed" : "offline"); return; }
    // Five consecutive unsuccessful connections are enough to identify a
    // persistent failure. Only an explicit retry/sign-in starts a new budget.
    if (this.retry >= 4) {
      this.exhausted = true;
      this.enabled = false;
      this.signal("reconnectFailed");
      return;
    }
    this.signal("reconnecting");
    const delay = Math.min(15000, 500 * 2 ** Math.min(5, this.retry++)) + Math.random() * 300;
    this.timer = setTimeout(() => this.connect(), delay);
  }
  clearWaiters() {
    for (const [id, w] of this.waiters) {
      clearTimeout(w.timer);
      w.resolve({ id, status: "outcomeUnknown", code: "GAME_DISCONNECTED" });
    }
    this.waiters.clear();
  }
  stop() {
    this.connectionRevision++;
    this.healthRequest?.controller.abort();
    this.enabled = false;
    clearTimeout(this.timer);
    this.socketCleanup?.();
    this.socketCleanup = null;
    this.disconnectSocket = null;
    const socket = this.socket;
    this.socket = null;
    socket?.close();
    this.clearWaiters();
    this.signal("offline");
  }
  logout() {
    this.authRevision++;
    this.stop();
    this.user = null;
    if (this.logoutRequest) return this.logoutRequest;
    const work = this.request("/api/logout", { method: "POST" }, async r => {
      if (!r.ok) throw new Error("LOGOUT_FAILED");
      await this.emptyBody(r);
    });
    this.logoutRequest = work;
    return work.finally(() => { if (this.logoutRequest === work) this.logoutRequest = null; });
  }
  async accounts() {
    return this.request("/api/accounts", {}, async r => {
      await this.accountResponse(r);
      const accounts = await r.json();
      if (!Array.isArray(accounts)) throw new Error("HOST_BAD_RESPONSE");
      return accounts;
    });
  }
  async accountResponse(r) {
    if (r.ok) return;
    const fallback = { 400: "INVALID_ACCOUNT", 401: "AUTH_REQUIRED", 403: "FORBIDDEN", 404: "ACCOUNT_NOT_FOUND", 409: "ACCOUNT_EXISTS", 429: "RATE_LIMITED" }[r.status] || "SERVER_UNAVAILABLE";
    const detail = r.headers?.get("content-type")?.includes("json") ? await r.json() : null;
    const allowed = ["INVALID_ACCOUNT", "AUTH_REQUIRED", "FORBIDDEN", "ACCOUNT_NOT_FOUND", "ACCOUNT_EXISTS", "ACCOUNT_LIMIT", "RATE_LIMITED"];
    throw new Error(allowed.includes(detail?.code) ? detail.code : fallback);
  }
  createAccount(body) {
    return this.request("/api/accounts", {
      method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(body),
    }, async r => { await this.accountResponse(r); await this.emptyBody(r); }, 10000);
  }
  updateAccount(name, body) {
    return this.request("/api/accounts/" + encodeURIComponent(name), {
      method: "PUT", headers: { "Content-Type": "application/json" }, body: JSON.stringify(body),
    }, async r => { await this.accountResponse(r); await this.emptyBody(r); }, 10000);
  }
  deleteAccount(name) {
    return this.request("/api/accounts/" + encodeURIComponent(name), { method: "DELETE" }, async r => {
      await this.accountResponse(r); await this.emptyBody(r);
    });
  }
  connectionInfo() {
    return this.request("/api/connection", {}, async r => { await this.accountResponse(r); return r.json(); });
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
  async route(from, to, train = "", signal, via = [], context = {}) {
    const query = new URLSearchParams({ from, to, train });
    if (via?.length) query.set("via", via.join(","));
    for(const [key,value] of Object.entries(context))if(value!=null)query.set(key,String(value));
    try {
    return await this.request(
      "/api/route?" + query,
      { signal }, async r => {
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
    return await r.json();
    }, 12000);
    } catch (error) {
      if (error.message === "HOST_TIMEOUT") throw new Error("ROUTE_CALCULATION_TIMEOUT");
      throw error;
    }
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
      activityRevision: this.activityRevision,
      reportActivity: this.activityRevision !== this.reportedActivity,
      controller: new globalThis.AbortController(),
    };
    const current = () =>
      !request.controller.signal.aborted &&
      request.user === this.user &&
      request.revision === this.authRevision;
    this.healthRequest = request;
    request.promise = (async () => {
      try {
        const options = { signal: request.controller.signal };
        if (request.reportActivity) options.headers = { "X-ADS-Activity": "1" };
        return await this.request("/api/health", {
          ...options,
        }, async r => {
        if (!current()) return null;
        if (r.status === 401) {
          this.authRevision++;
          this.stop();
          this.user = null;
          this.dispatchEvent(new Event("auth-expired"));
          return null;
        }
        const result = r.ok ? await r.json() : null;
        if (!r.ok && current()) throw new Error("HOST_UNAVAILABLE");
        if (current() && request.reportActivity && this.activityRevision === request.activityRevision)
          this.reportedActivity = request.activityRevision;
        return current() ? result : null;
        }, 5000);
      } catch (error) {
        if (!current()) return null;
        this.disconnectSocket?.(error.message === "HOST_TIMEOUT" ? "HOST_TIMEOUT" : "HOST_UNAVAILABLE");
        throw error;
      } finally {
        if (this.healthRequest === request) this.healthRequest = null;
      }
    })();
    return request.promise;
  }
}
