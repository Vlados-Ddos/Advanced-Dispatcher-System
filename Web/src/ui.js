import { matchesSearch, normalizeSearch, tabLabel } from "./entity-search.js";
import { wagonRows, wagonGroupSummary } from "./wagon-groups.js";
import {
  isWagon,
  isLocomotive,
  trainDescription,
  vehicleHeading,
} from "./rolling-stock.js";
import { previewWarnings } from "./route-preview.js";
import { uiIcon } from "./ui-icons.js";
import { groupedJobs } from "./job-groups.js";
import { renderEvent } from "./event-details.js";
import { jobIcon } from "./job-icons.js";
import { isJobAction, jobEventContent } from "./job-events.js";
import { renderSignalControls } from "./signal-controls.js";
import { countText } from "./localization.js";
import { isSignal, railwaySigns } from "./railway-objects.js";
import { saveSetting } from "./storage.js";
import {
  jobColor,
  colorLegend,
  jobTypeText,
  jobTypeKey,
  primaryJob,
} from "./job-display.js";
import { allLocations, renderLocations, locationDetails } from "./locations.js";
import { renderLayers, saveLayers } from "./layers.js";
import {
  entityName,
  trackName,
  locationName,
  targetName,
  localizedValue,
} from "./display-names.js";
import { signalCategory, aspectText } from "./signal-display.js";
import {
  currentWarning,
  warningDetails,
  reservationActions,
  routeMatches,
} from "./route-details.js";
import {
  t,
  number,
  language,
  displayName,
  compareNames,
} from "./localization.js";
import { $, el, syncChildren } from "./dom.js";
import { listWindow } from "./virtual-list.js";
import { rowHeight } from "./preferences.js";
import {
  tickClocks,
  timer,
  renderDashboard,
  activeOrders,
  renderFilters,
  displaySettings,
  jobDetails,
  routeDetails,
  extraInspector,
} from "./ui-details.js";
export class UI {
  constructor(store, network, renderer) {
    this.store = store;
    this.network = network;
    this.renderer = renderer;
    this.tab = "trains";
    this.query = "";
    this.selected = null;
    this.items = [];
    this.dirty = true;
    this.lastList = 0;
    this.startTrack = null;
    this.endTrack = null;
    this.routeVia = [];
    this.routeWaypointPicking = false;
    this.user = null;
    this.health = null;
    this.signalFilter = "all";
    this.tabQueries = new Map();
    this.tabFilters = new Map();
    this.pendingCommands = 0;
    this.planningError = null;
    this.bind();
    setInterval(() => this.refresh(), 250);
  }
  bind() {
    for (const key of [
      "locations",
      "trains",
      "cars",
      "signals",
      "switches",
      "turntables",
      "blocks",
      "routes",
      "jobs",
      "players",
      "tracks",
      "signs",
      "log",
      "settings",
    ]) {
      const button = el("button");
      const icon = el("span", undefined, "nav-icon");
      icon.append(uiIcon(key));
      button.append(icon, el("span", t(tabLabel(key)), "nav-label"));
      button.title = t(tabLabel(key));
      button.setAttribute("aria-label", t(tabLabel(key)));
      button.dataset.tab = key;
      button.addEventListener("click", () => this.setTab(key));
      $("tabs").append(button);
    }
    $("search").addEventListener("input", (e) => {
      this.query = normalizeSearch(e.target.value);
      this.wagonSearchCollapsed?.clear();
      $("object-list").scrollTop = 0;
      this.dirty = true;
      if (!this.searchFrame)
        this.searchFrame = requestAnimationFrame(() => {
          this.searchFrame = null;
          this.lastList = performance.now();
          this.dirty = false;
          this.collect();
          this.renderRows();
          activeOrders(this);
        });
    });
    $("active-only").addEventListener("change", () => (this.dirty = true));
    $("object-list").addEventListener("scroll", () => this.renderRows());
    $("fit").onclick = () => this.renderer.fit();
    $("home-player").onclick = () => {
      const players = [...this.store.players.values()].map((p) =>
        this.renderer.store.playerPosition(p, this.renderer.frameTime),
      );
      if (players.length)
        this.renderer.center(
          players.reduce((n, p) => n + p.x, 0) / players.length,
          players.reduce((n, p) => n + p.z, 0) / players.length,
          0.3,
        );
    };
    $("follow").onclick = () => {
      this.renderer.follow = this.renderer.follow ? null : this.selected;
      $("follow").classList.toggle("active", !!this.renderer.follow);
    };
    $("zoom-in").onclick = () => this.renderer.zoom(1.4);
    $("zoom-out").onclick = () => this.renderer.zoom(1 / 1.4);
    $("collapse").onclick = () => document.body.classList.toggle("collapsed");
    $("theme-button").onclick = () => {
      document.body.classList.toggle("light");
      saveSetting(
        "ads.theme",
        document.body.classList.contains("light") ? "light" : "dark",
      );
      this.renderer.theme();
    };
    $("layers-button").onclick = () => {
      $("layers").hidden = !$("layers").hidden;
      $("layers-button").setAttribute(
        "aria-expanded",
        String(!$("layers").hidden),
      );
    };
    $("layers-button").setAttribute("aria-controls", "layers");
    $("layers").onkeydown = (event) => {
      if (event.key === "Escape") {
        $("layers").hidden = true;
        $("layers-button").setAttribute("aria-expanded", "false");
        $("layers-button").focus();
      }
    };
    $("metrics-button").onclick = () => {
      $("metrics").hidden = !$("metrics").hidden;
    };
    this.renderer.addEventListener("select", (e) => {
      if (
        this.selected?.id !== e.detail?.id ||
        this.selected?.kind !== e.detail?.kind
      ) {
        this.signalAspectDraft = null;
        this.signalModeDraft = null;
      }
      this.selected = e.detail;
      if (this.selected?.kind === "routes" && this.startTrack) {
        this.store.preview = null;
        this.startTrack = this.endTrack = this.startTrain = null;
        this.routeVia = [];
        this.routeWaypointPicking = false;
        this.renderRoute();
      }
      if (
        this.routeWaypointPicking &&
        e.detail?.kind === "tracks" &&
        this.startTrack &&
        this.endTrack
      ) {
        this.routeWaypointPicking = false;
        this.addWaypoint(e.detail.id);
        return;
      }
      this.renderInspector();
      this.renderRows();
      this.dirty = true;
    });
    this.renderer.addEventListener("route-focus", () => {
      this.renderInspector();
      this.layers();
    });
    $("exit-route-focus").onclick = () => this.renderer.clearRouteFocus();
    this.store.addEventListener("change", (e) => {
      const { kind, payload: p } = e.detail;
      if (["routes", "snapshot", "topology"].includes(kind))
        this.selectPlannedRoute();
      if (
        this.selected?.kind === "tracks" &&
        (["routes", "snapshot", "topology"].includes(kind) ||
          (kind === "delta" &&
            (p.occupancy?.length ||
              p.blocks?.length ||
              p.removedBlocks?.length)))
      )
        this.inspectorDirty = true;
      if (
        this.tab === "settings" &&
        (["snapshot", "topology"].includes(kind) ||
          (kind === "delta" &&
            (p.jobs?.length ||
              p.replaceJobs ||
              p.cars?.length ||
              p.removedCars?.length ||
              p.locations?.length ||
              p.replaceLocations)))
      ) {
        const legend = $("settings-panel").querySelector(
          '[data-key="car-color-legend"]',
        );
        if (legend)
          syncChildren(
            legend,
            colorLegend(this.store, this.renderer.colorMode),
          );
      }
      if (kind === "receipt") return;
      if (kind === "event" && isJobAction(p)) {
        const node = this.toast(jobEventContent(this, p));
        node.jobEvent = p;
      }
      if (kind === "routes") {
        const selectedRoute =
          this.selected?.kind === "routes" &&
          this.store.routes.find((r) => r.id === this.selected.id);
        if (
          this.selected?.kind === "routes" &&
          (!selectedRoute || (selectedRoute.endedAt && !this.selected.endedAt))
        )
          this.renderer.select(null);
        if (this.tab === "routes") {
          this.collect();
          this.renderRows();
        }
      }
      const changed = (tab) =>
        kind === "snapshot" ||
        kind === "topology" ||
        (kind === "delta" &&
          (p.reset ||
            (tab === "trains" || tab === "cars" || tab === "wagonGroups"
              ? p.cars?.length || p.motions?.length || p.removedCars?.length
              : p[tab]?.length ||
                (tab === "jobs" && p.replaceJobs) ||
                (tab === "players" && p.replacePlayers) ||
                ((tab === "signals" || tab === "signs") &&
                  p.removedSignals?.length) ||
                (tab === "signs" && p.signals?.length) ||
                (tab === "blocks" && p.removedBlocks?.length)))) ||
        (kind === "event" && tab === "log") ||
        (kind === "routes" && tab === "routes") ||
        (tab === "locations" &&
          kind === "delta" &&
          (p.replaceLocations || p.locations?.length));
      if (kind === "snapshot" || kind === "topology") {
        this.lastList = 0;
        this.signalAspectDraft = null;
      }
      if (changed(this.tab)) this.dirty = true;
      if (
        kind === "delta" &&
        (p.replacePlayers || p.players?.length) &&
        this.tab === "jobs" &&
        this.sort === "player"
      )
        this.dirty = true;
      if (kind === "delta" && (p.replaceJobs || p.jobs?.length)) {
        if (
          this.tab === "jobs" &&
          this.jobTypeSignature !==
            [
              ...new Set(
                [...this.store.jobs.values()].map((j) =>
                  [j.type, j.typeName, j.typeLanguage].join(":"),
                ),
              ),
            ]
              .sort()
              .join("|")
        )
          renderFilters(this);
        if (["cars", "trains", "jobs"].includes(this.selected?.kind))
          this.inspectorDirty = true;
      }
      if (
        this.selected &&
        ((this.selected.kind === "signals" && kind === "delta"
          ? p.reset ||
            p.removedSignals?.includes(this.selected.id) ||
            p.signals?.some(
              (s) =>
                s.id === this.selected.id ||
                (s.controller &&
                  s.controller ===
                    this.store.signals.get(this.selected.id)?.controller),
            )
          : changed(this.selected.kind)) ||
          (this.selected.kind === "warnings" && kind === "routes"))
      )
        this.inspectorDirty = true;
      if (kind === "delta" && this.selected?.kind === "jobs") {
        const ids = this.store.jobs.get(this.selected.id)?.cars || [];
        if (
          p.cars?.some((c) => ids.includes(c.id)) ||
          p.removedCars?.some((id) => ids.includes(id))
        )
          this.inspectorDirty = true;
      }
      if (kind === "capabilities" || kind === "connection") {
        this.inspectorDirty = true;
        this.dirty = true;
      }
      if (
        kind === "topology" ||
        (kind === "snapshot" &&
          this.startTrack &&
          (!this.store.tracks.has(this.startTrack) ||
            (this.endTrack && !this.store.tracks.has(this.endTrack)) ||
            this.routeVia.some((id) => !this.store.tracks.has(id))))
      ) {
        this.startTrack = this.endTrack = this.startTrain = null;
        this.routeVia = [];
        this.routeWaypointPicking = false;
        this.clearRoutePreview();
        this.renderRoute();
      }
      if (this.inspectorDirty && this.selected?.kind === "signals")
        this.scheduleInspector();
      if (
        !this.planning &&
        !this.plannedRoute &&
        this.previewAffected(kind, p) &&
        ((this.previewKey && this.previewKey !== this.previewStateKey()) ||
          (kind === "connection" &&
            !this.store.disconnected &&
            this.startTrack &&
            this.endTrack))
      )
        if (this.previewPending && !this.store.disconnected) {
          this.store.preview = null;
          this.renderer.overlayDirty = true;
          this.renderRoute();
        } else this.previewRoute();
    });
    window.addEventListener("ads-display", () => {
      this.dirty = true;
      this.lastList = 0;
      this.renderRows();
    });
    this.network.addEventListener("status", (e) => this.connection(e.detail));
    window.addEventListener("ads-language", () => {
      for (const node of $("toasts").children)
        if (node.jobEvent)
          node.replaceChildren(jobEventContent(this, node.jobEvent));
      for (const b of $("tabs").children) {
        b.querySelector(".nav-label").textContent = t(tabLabel(b.dataset.tab));
        b.title = t(tabLabel(b.dataset.tab));
        b.setAttribute("aria-label", t(tabLabel(b.dataset.tab)));
      }
      $("panel-heading").textContent = t(tabLabel(this.tab));
      this.layers();
      this.signalSubtabs();
      renderFilters(this);
      this.dirty = true;
      this.renderInspector();
      this.renderRoute();
      this.connection(this.network.status);
      if (this.user)
        $("account-name").textContent =
          (this.user.name === "local-owner"
            ? t("localOwner")
            : this.user.name) +
          " · " +
          t(this.user.role);
      if (this.tab === "settings") this.settings();
    });
    this.layers();
    this.setTab("trains");
  }
  connection(status) {
    $("connection").className =
      "connection " + (status === "connected" ? "live" : "offline");
    $("connection").lastElementChild.textContent = t(status);
  }
  setUser(user) {
    this.user = user;
    $("account-name").textContent = user
      ? (user.name === "local-owner" ? t("localOwner") : user.name) +
        " · " +
        t(user.role)
      : "";
    this.renderInspector();
    if (this.tab === "settings") this.settings();
  }
  setTab(tab, options = {}) {
    this.renderer.cancelPan?.();
    this.tabQueries.set(this.tab, this.query);
    this.tabFilters.set(this.tab, {
      status: $("status-filter").value,
      active: $("active-only").checked,
      sort: this.sort,
    });
    const changed = tab !== this.tab;
    this.query =
      options.query ?? (changed ? this.tabQueries.get(tab) || "" : this.query);
    this.query = normalizeSearch(this.query);
    $("search").value = this.query;
    this.tab = tab;
    this.renderer.trackPicking = tab === "tracks" || !!this.startTrack;
    document.body.classList.remove("collapsed");
    $("panel-heading").textContent = t(tabLabel(tab));
    $("panel-heading").removeAttribute("data-i18n");
    if (changed) $("active-only").checked = false;
    $("object-list").scrollTop = 0;
    for (const b of $("tabs").children) {
      b.classList.toggle("active", b.dataset.tab === tab);
      b.setAttribute("aria-current", b.dataset.tab === tab ? "page" : "false");
    }
    $("settings-panel").hidden = tab !== "settings";
    $("list-controls").hidden = tab === "settings";
    $("object-list").hidden = ["settings", "locations"].includes(tab);
    $("location-list").hidden = tab !== "locations";
    $("active-only").parentElement.hidden = ![
      "cars",
      "trains",
      "signals",
      "blocks",
      "routes",
      "jobs",
    ].includes(tab);
    $("list-title").textContent = t(tabLabel(tab));
    document.querySelector(".list-heading").hidden = tab === "settings";
    document.querySelector(".search").hidden = tab === "settings";
    if (tab === "settings") this.settings();
    this.sort =
      this.tabFilters.get(tab)?.sort || (tab === "log" ? "recent" : "name");
    renderFilters(this);
    const filters = this.tabFilters.get(tab);
    if (filters) {
      if (
        [...$("status-filter").options].some((o) => o.value === filters.status)
      )
        $("status-filter").value = filters.status;
      $("active-only").checked = filters.active;
      this.sort = filters.sort;
      $("sort-order").value = this.sort || "name";
    }
    this.signalSubtabs();
    activeOrders(this);
    this.dirty = true;
    if (tab !== "settings") {
      this.collect();
      this.renderRows();
    }
  }
  layers() {
    renderLayers(this, $("layers"));
  }
  signalSubtabs() {
    const root = $("signal-subtabs");
    if (!root) return;
    root.hidden = this.tab !== "signals";
    if (root.hidden) return;
    root.replaceChildren();
    for (const [key, label] of [
      ["all", "signalsAll"],
      ["standard", "signalsStandard"],
      ["shunting", "shuntingSignals"],
    ]) {
      const button = el("button", t(label), "signal-subtab");
      button.classList.toggle("active", this.signalFilter === key);
      button.onclick = () => {
        this.signalFilter = key;
        $("object-list").scrollTop = 0;
        this.dirty = true;
        this.signalSubtabs();
      };
      root.append(button);
    }
    const label = el("label", undefined, "signal-layer-toggle");
    const input = el("input");
    input.type = "checkbox";
    input.checked = this.renderer.layers.shuntingSignals;
    input.id = "show-shunting-signals";
    input.onchange = () => {
      this.renderer.layers.shuntingSignals = input.checked;
      saveLayers(this.renderer.layers);
      this.renderer.invalidate();
      this.layers();
    };
    label.append(input, el("span", t("showShuntingSignals")));
    root.append(label);
    root.append(el("small", t("displayOnly"), "signal-layer-note"));
  }
  refresh() {
    if (document.hidden) return;
    const mapScale = $("display-mapScale");
    if (
      mapScale &&
      document.activeElement !== mapScale &&
      Number(mapScale.value) !== Math.log(this.renderer.scale)
    )
      mapScale.value = Math.log(this.renderer.scale);
    const enabled =
      this.store.canControl && !!this.user && this.user.role !== "viewer";
    for (const control of document.querySelectorAll("[data-mutating]"))
      control.disabled =
        !enabled ||
        (this.pendingCommands > 0 && control.dataset.interrupt !== "true") ||
        control.dataset.unavailable === "true" ||
        (control.dataset.admin === "true" &&
          (this.user?.role !== "admin" ||
            !this.store.capabilities.locoControls));
    if (enabled !== this.controlsEnabled) {
      this.controlsEnabled = enabled;
      this.inspectorDirty = true;
    }
    const caps = this.store.capabilities,
      mode = caps.mode;
    this.renderer.root.classList.toggle("stale", this.store.stale);
    const notice = $("notice");
    let text = "";
    if (this.health?.accountsRecovery) text = t("accountRecovery");
    else if (mode === "demo") text = t("demo");
    else if (this.store.topology && this.store.stale) text = t("stale");
    else if (mode === "client") text = t("hostOnly");
    if (notice.textContent !== text) notice.textContent = text;
    notice.hidden = !text;
    $("network-counts").textContent =
      countText(this.store.tracks.size, "trackCount") +
      " · " +
      countText(
        [...this.store.signals.values()].filter(isSignal).length,
        "signalCount",
      );
    if (this.dirty && performance.now() - this.lastList > 500) {
      this.lastList = performance.now();
      this.dirty = false;
      this.collect();
      this.renderRows();
      activeOrders(this);
    }
    if (this.inspectorDirty) this.scheduleInspector();
    tickClocks(this);
    if (performance.now() - (this.lastDashboard || 0) > 1000) {
      this.lastDashboard = performance.now();
      renderDashboard(this);
    }
    if (!$("metrics").hidden) {
      $("metrics").replaceChildren();
      for (const [key, value] of [
        ["fps", this.renderer.fps],
        ["renderer", number(this.renderer.renderMs, 2) + " " + t("ms")],
        ["capture", number(caps.captureMs, 2) + " " + t("ms")],
        ["received", number(this.network.bytes / 1048576, 2) + " MiB"],
        ["clients", this.health?.clients ?? "—"],
        ["age", number(this.health?.stateAgeMs ?? 0) + " " + t("ms")],
      ]) {
        const row = el("div");
        row.append(el("span", t(key) + ": "), el("strong", String(value)));
        $("metrics").append(row);
      }
    }
  }
  collect() {
    const s = this.store;
    let result;
    if (this.tab === "locations") result = allLocations(s);
    else if (this.tab === "signs") result = railwaySigns(s);
    else if (this.tab === "signals")
      result = [...s.signals.values()].filter(isSignal);
    else if (this.tab === "cars") result = [...s.cars.values()].filter(isWagon);
    else if (this.tab === "trains")
      result = [...s.cars.values()].filter(isLocomotive);
    else if (this.tab === "switches")
      result = [...s.junctions.values()].map((j) => ({
        ...j,
        ...s.switches.get(j.id),
      }));
    else if (this.tab === "turntables")
      result = [...s.tableDefs.values()].map((def) => ({
        ...def,
        ...s.turntables.get(def.id),
      }));
    else if (this.tab === "routes") result = [...s.routes];
    else if (this.tab === "log")
      result = [...s.events].reverse().map((e) => ({ ...e, name: t(e.code) }));
    else if (this.tab === "blocks" && s.blocks.size === 0)
      result = [...s.tracks.values()].map((track) => ({
        id: track.id,
        name: track.name,
        source: "dispatch",
        tracks: [track.id],
        length: track.length,
        occupied: s.occupancy.get(track.id)?.occupied,
        quality: s.occupancy.has(track.id) ? "ready" : "unknown",
      }));
    else result = [...(s[this.tab]?.values() || [])];
    if (this.tab === "signals" && this.signalFilter !== "all")
      result = result.filter((signal) =>
        this.signalFilter === "shunting"
          ? signalCategory(signal) === "shunting"
          : signalCategory(signal) !== "shunting",
      );
    const active =
      !$("active-only").parentElement.hidden && $("active-only").checked;
    this.items = result
      .filter(Boolean)
      .filter(
        (item) =>
          this.tab === "cars" ||
          matchesSearch(this.store, this.tab, item, this.query),
      )
      .filter(
        (item) =>
          !active ||
          (this.tab === "jobs"
            ? item.state === "InProgress" || (item.active && !item.state)
            : this.tab === "signals"
              ? item.stop || item.reserved
              : this.tab === "blocks"
                ? item.occupied || item.reserved
                : this.tab === "routes"
                  ? !item.endedAt
                  : Math.abs(item.speed || 0) > 0.2),
      );
    const filter = $("status-filter").value;
    if (filter && filter !== "all")
      this.items = this.items.filter((item) =>
        this.tab === "jobs"
          ? item.state === filter || "type:" + jobTypeKey(item) === filter
          : this.tab === "routes"
            ? routeMatches(item, filter)
            : item.type === filter || item.severity === filter,
      );
    const sort = this.sort || (this.tab === "log" ? "recent" : "name");
    const names =
      sort === "name" || sort === "player"
        ? new Map(
            this.items.map((item) => [
              item.id,
              entityName(
                this.store,
                item.kind === "wagonGroups"
                  ? "wagonGroups"
                  : this.tab === "trains"
                    ? "cars"
                    : this.tab,
                item,
              ),
            ]),
          )
        : null;
    this.items.sort((a, b) =>
      sort === "speed"
        ? Math.abs(b.speed || 0) - Math.abs(a.speed || 0)
        : sort === "recent"
          ? (b.time || b.sampledAt || b.createdAt || 0) -
            (a.time || a.sampledAt || a.createdAt || 0)
          : sort === "status"
            ? String(a.state || a.aspect || a.status || "").localeCompare(
                String(b.state || b.aspect || b.status || ""),
              )
            : compareNames(names.get(a.id), names.get(b.id)),
    );
    this.jobGroupsCollapsed ||= new Set();
    this.listRows =
      this.tab === "jobs" && sort === "player"
        ? groupedJobs(this.items, this.store, this.jobGroupsCollapsed)
        : this.items;
    if (this.tab === "cars") {
      this.wagonGroupsExpanded ||= new Set();
      this.wagonSearchCollapsed ||= new Set();
      if (this.lastWagonQuery !== this.query) {
        this.wagonSearchCollapsed.clear();
        this.lastWagonQuery = this.query;
      }
      const grouped = wagonRows(
        s,
        this.items,
        this.wagonGroupsExpanded,
        this.query,
        this.wagonSearchCollapsed,
      );
      this.listRows = grouped.rows;
      this.items = this.items.filter((c) => grouped.matched.has(c.id));
    }
    $("list-title").textContent = t(tabLabel(this.tab));
    $("list-count").textContent = number(this.items.length);
    const note = $("selected-job-note");
    note.hidden =
      this.tab !== "jobs" ||
      this.selected?.kind !== "jobs" ||
      this.items.some((j) => j.id === this.selected.id);
    note.textContent = t("selectedJobOutsideFilter");
  }
  subtitle(item) {
    if (item.kind === "wagonGroups") return wagonGroupSummary(this.store, item);
    if (this.tab === "trains")
      return [item.model, item.job].filter(Boolean).join(" · ");
    if (this.tab === "cars")
      return item.job || localizedValue(item.type, "rollingStock");
    if (this.tab === "signals")
      return aspectText(item) + " · " + localizedValue(item.mode);
    if (this.tab === "blocks")
      return (
        (item.source === "dispatch" ? t("section") : item.source) +
        " · " +
        (item.quality === "unknown"
          ? t("unknown")
          : item.occupied
            ? t("occupied")
            : t("free"))
      );
    if (this.tab === "switches")
      return this.store.stale ||
        !Number.isInteger(item.branch) ||
        item.branch < 0
        ? t("unknown")
        : t("branch") + " " + (item.branch + 1);
    if (this.tab === "routes")
      return (
        t(item.status || "planned") +
        (item.reservationState === "reserved"
          ? " · " + t("reservation_" + item.reservationMode)
          : "") +
        " · " +
        number(item.length) +
        " " +
        t("meters")
      );
    if (this.tab === "jobs")
      return (
        jobTypeText(item) +
        " · " +
        t(item.state || "Available") +
        " · " +
        (item.origin ? locationName(this.store, item.origin) : "—") +
        " → " +
        (item.destination ? locationName(this.store, item.destination) : "—")
      );
    if (this.tab === "players") return item.host ? t("host") : t("client");
    if (this.tab === "tracks") return number(item.length) + " " + t("meters");
    if (this.tab === "log")
      return (
        new Date(item.time).toLocaleTimeString(language()) +
        " · " +
        (item.actor || "") +
        " · " +
        (item.target ? targetName(this.store, item.target) : "")
      );
    return "";
  }
  renderRows() {
    if (this.tab === "settings") return;
    if (this.tab === "locations") {
      renderLocations(this);
      return;
    }
    const root = $("object-list"),
      height = rowHeight();
    // A collapsed panel has no viewport. Retain its keyed rows until it is
    // visible again instead of shrinking/recreating the virtual window at 0px.
    if (root.hidden || root.clientHeight <= 0) return;
    if (!this.listSurface) {
      this.listSurface = el("div", undefined, "list-surface");
      this.listWindow = el("div", undefined, "list-window");
      this.listSurface.append(this.listWindow);
      root.replaceChildren(this.listSurface);
    }
    const scaledTop = this.lastRowHeight
      ? (root.scrollTop * height) / this.lastRowHeight
      : root.scrollTop;
    this.lastRowHeight = height;
    const rows =
      (this.tab === "jobs" && this.sort === "player") || this.tab === "cars"
        ? this.listRows || this.items
        : this.items;
    const { start, end, top, contentHeight } = listWindow(
      rows.length,
      height,
      root.clientHeight,
      scaledTop,
    );
    this.listSurface.style.height = contentHeight + "px";
    this.listWindow.style.top = start * height + "px";
    if (root.scrollTop !== top) root.scrollTop = top;
    const selected = this.selected?.kind + ":" + this.selected?.id,
      locale = language();
    const prior = this.rowRender;
    if (
      prior?.rows === rows &&
      prior.start === start &&
      prior.end === end &&
      prior.height === height &&
      prior.selected === selected &&
      prior.locale === locale
    )
      return;
    this.rowRender = { rows, start, end, height, selected, locale };
    const fragment = document.createDocumentFragment();
    for (let i = start; i < end; i++) {
      const item = rows[i];
      if (item.group) {
        const wrapper = el("div", undefined, "virtual-row job-group");
        wrapper.dataset.key = item.id;
        wrapper.style.height = height + "px";
        wrapper.setAttribute("role", "listitem");
        const button = el(
          "button",
          item.name + " · " + number(item.count),
          "job-group-heading",
        );
        button.setAttribute(
          "aria-expanded",
          String(!this.jobGroupsCollapsed.has(item.key)),
        );
        button.onclick = () => {
          if (!this.jobGroupsCollapsed.delete(item.key))
            this.jobGroupsCollapsed.add(item.key);
          this.collect();
          this.renderRows();
        };
        wrapper.append(button);
        fragment.append(wrapper);
        continue;
      }
      const rowKind =
        item.kind === "wagonGroups"
          ? "wagonGroups"
          : this.tab === "trains"
            ? "cars"
            : this.tab;
      const row = el("button", undefined, "object-row");
      row.dataset.entity = item.id;
      row.setAttribute(
        "aria-label",
        entityName(
          this.store,
          item.kind === "wagonGroups"
            ? "wagonGroups"
            : this.tab === "trains"
              ? "cars"
              : this.tab,
          item,
        ),
      );
      row.title =
        entityName(
          this.store,
          item.kind === "wagonGroups"
            ? "wagonGroups"
            : this.tab === "trains"
              ? "cars"
              : this.tab,
          item,
        ) +
        " · " +
        this.subtitle(item);
      row.classList.toggle(
        "selected",
        this.selected?.kind === rowKind && this.selected.id === item.id,
      );
      const icon = el("span", undefined, "object-icon");
      icon.append(uiIcon(this.tab));
      if (this.tab === "jobs") {
        icon.replaceChildren(jobIcon(item));
        icon.classList.add("job-icon");
        icon.style.color = jobColor(item);
      }
      row.append(icon);
      const label = el("span");
      label.append(
        el(
          "strong",
          entityName(
            this.store,
            item.kind === "wagonGroups"
              ? "wagonGroups"
              : this.tab === "trains"
                ? "cars"
                : this.tab,
            item,
          ),
        ),
        el("small", this.subtitle(item)),
      );
      row.append(label);
      if (item.speed !== undefined)
        row.append(
          el(
            "span",
            number(Math.abs(item.speed), 0) + " " + t("kmh"),
            "row-value",
          ),
        );
      if (this.tab === "jobs" && item.active) row.append(timer(item));
      row.onclick = () => {
        const current =
          rowKind === "wagonGroups"
            ? this.store.wagonGroup(item.id)
            : rowKind === "cars"
              ? this.store.cars.get(item.id)
              : item;
        if (current) this.pick(rowKind, current);
      };
      const wrapper = el("div");
      wrapper.className = "virtual-row";
      wrapper.style.height = height + "px";
      wrapper.dataset.key = rowKind + ":" + item.id;
      if (item.wagonChild) wrapper.classList.add("wagon-child");
      wrapper.setAttribute("role", "listitem");
      wrapper.append(row);
      if (rowKind === "wagonGroups") {
        wrapper.classList.add("wagon-group-row");
        row.title = t("wagonGroupHint");
        const expanded =
          !this.wagonSearchCollapsed.has(item.id) &&
          (this.wagonGroupsExpanded.has(item.id) || !!this.query);
        const toggle = el("button", undefined, "wagon-toggle");
        toggle.append(uiIcon("chevron"));
        toggle.setAttribute("aria-expanded", String(expanded));
        toggle.setAttribute(
          "aria-label",
          t(expanded ? "collapseWagons" : "expandWagons"),
        );
        toggle.onclick = () => {
          if (this.query) {
            if (!this.wagonSearchCollapsed.delete(item.id))
              this.wagonSearchCollapsed.add(item.id);
          } else if (!this.wagonGroupsExpanded.delete(item.id))
            this.wagonGroupsExpanded.add(item.id);
          this.collect();
          this.renderRows();
        };
        wrapper.append(toggle);
      }
      fragment.append(wrapper);
    }
    if (!this.items.length)
      fragment.append(
        el("p", t(this.query ? "noMatch" : "empty"), "empty-list"),
      );
    syncChildren(this.listWindow, fragment);
  }
  pick(kind, item) {
    if (this.selected?.id !== item.id || this.selected?.kind !== kind) {
      this.signalAspectDraft = null;
      this.signalModeDraft = null;
    }
    if (kind === "routes") {
      this.store.preview = null;
      this.startTrack = this.endTrack = this.startTrain = null;
      this.renderRoute();
    }
    this.renderer.select({ kind, id: item.id, endedAt: item.endedAt });
    this.renderInspector();
    if (document.body.classList.contains("compact"))
      document.body.classList.add("collapsed");
    if (this.tab === kind) this.renderRows();
    this.dirty = true;
  }
  location(id) {
    return allLocations(this.store).find(
      (s) => s.id === id || s.id === "pj:" + id,
    );
  }
  openJob(job) {
    if (!this.store.jobs.has(job.id)) return this.toast(t("jobUnavailable"));
    this.setTab("jobs");
    this.pick("jobs", this.store.jobs.get(job.id));
    this.collect();
    this.renderRows();
  }
  kv(root, key, value) {
    const row = el("div", undefined, "kv");
    row.dataset.key = "field-" + key;
    row.append(el("span", t(key)), el("span", displayName(value ?? "—")));
    root.append(row);
  }
  button(root, label, action, mutating = false, available = true) {
    const b = el("button", t(label));
    b.dataset.key = "action-" + label;
    b.disabled = !available;
    if (label === "cancelRoute") b.dataset.interrupt = "true";
    if (["couple", "uncouple"].includes(label)) b.dataset.admin = "true";
    if (mutating) {
      b.dataset.mutating = "true";
      b.dataset.unavailable = String(!available);
      b.disabled =
        !available ||
        !this.store.canControl ||
        !this.user ||
        this.user.role === "viewer" ||
        (this.pendingCommands > 0 && b.dataset.interrupt !== "true");
    }
    b.onclick = action;
    root.append(b);
    return b;
  }
  scheduleInspector() {
    if (this.inspectorFrame) return;
    this.inspectorFrame = requestAnimationFrame(() => {
      this.inspectorFrame = null;
      if (this.inspectorDirty) this.renderInspector();
    });
  }
  async command(kind, fields) {
    if (this.pendingCommands > 0 && kind !== "cancelRoute")
      return { status: "rejected", code: "COMMAND_PENDING" };
    this.pendingCommands++;
    this.refresh();
    try {
      const result = await this.network.command(kind, fields);
      this.toast(
        localizedValue(result.code || result.status, "COMMAND_FAILED") +
          (result.status !== "applied" && result.target
            ? " · " + targetName(this.store, result.target)
            : ""),
        result.status !== "applied",
      );
      for (const warning of result.warnings || [])
        this.toast(t(warning), false);
      this.dirty = true;
      this.inspectorDirty = true;
      return result;
    } catch (error) {
      this.toast(t("COMMAND_FAILED"), true);
      return { status: "outcomeUnknown", code: "COMMAND_FAILED" };
    } finally {
      this.pendingCommands--;
      this.refresh();
    }
  }
  toast(message, error = false) {
    const p = el("div", undefined, "toast" + (error ? " error" : ""));
    if (typeof message === "string") p.textContent = message;
    else p.append(message);
    $("toasts").append(p);
    while ($("toasts").children.length > 4) $("toasts").firstChild.remove();
    setTimeout(() => p.remove(), 10000);
    return p;
  }
  renderInspector() {
    this.inspectorDirty = false;
    const target = $("inspector"),
      root = el("div");
    if (!this.selected) {
      target.hidden = true;
      target.replaceChildren();
      return;
    }
    const { kind, id } = this.selected;
    let item = this.renderer.resolve(this.selected);
    if (kind === "warnings") item = currentWarning(this);
    if (kind === "routes") item = this.store.routes.find((r) => r.id === id);
    if (kind === "blocks")
      item = this.store.blocks.get(id) || this.items.find((x) => x.id === id);
    if (kind === "jobs") item = this.store.jobs.get(id);
    if (kind === "log") item = this.store.events.find((e) => e.id === id);
    if (!item) {
      this.selected = null;
      this.renderer.select(null);
      target.hidden = true;
      target.replaceChildren();
      return;
    }
    if (target.hidden) target.hidden = false;
    const header = el("header");
    const title = el("div");
    title.append(
      el("div", t(kind === "cars" ? vehicleHeading(item) : kind), "eyebrow"),
      el(
        "h2",
        kind === "log" || kind === "warnings"
          ? localizedValue(item.code, "eventRecorded")
          : entityName(this.store, kind, item),
      ),
    );
    const close = el("button", "×");
    close.setAttribute("aria-label", t("close"));
    close.onclick = () => {
      this.selected = null;
      this.renderer.select(null);
    };
    header.append(title, close);
    root.append(header);
    const actions = el("div", undefined, "actions");
    actions.dataset.key = "inspector-actions";
    if (kind === "warnings") warningDetails(this, root, item);
    if (kind === "locations") locationDetails(this, root, item);
    if (kind === "tracks") {
      this.kv(root, "length", number(item.length) + " " + t("meters"));
      root.append(el("p", t("selectTrackHint")));
      const related = el("div", undefined, "track-relations");
      related.dataset.key = "track-relations";
      const blockItems = [...this.store.blocks.values()]
          .filter(
            (block) =>
              [...(block.tracks || []), ...(block.extraTracks || [])].includes(id),
          )
          .sort((a, b) => entityName(this.store, "blocks", a).localeCompare(entityName(this.store, "blocks", b)));
      const routeItems = this.store.routes
        .filter(
          (route) =>
            !route.endedAt &&
            [...(route.tracks || []), ...(route.extraTracks || [])].includes(id),
        )
        .sort((a, b) => entityName(this.store, "routes", a).localeCompare(entityName(this.store, "routes", b)));
      const relationSection = (relationKind, objects, emptyKey, renderRow) => {
        const section = el("details", undefined, "relation-group");
        section.dataset.key = "track-relations-" + relationKind;
        section.open = true;
        const summary = el("summary");
        summary.append(
          el("strong", t(relationKind === "blocks" ? "relatedBlocks" : "relatedRoutes")),
          el("span", " " + number(objects.length), "relation-count"),
        );
        section.append(summary);
        const list = el("div", undefined, "relation-list");
        if (!objects.length) list.append(el("p", t(emptyKey), "empty-list"));
        for (const object of objects) list.append(renderRow(object));
        section.append(list);
        return section;
      };
      const relationButton = (relationKind, object) => {
        const row = el("div", undefined, "relation-row");
        row.dataset.key = relationKind + ":" + object.id;
        const button = el("button", entityName(this.store, relationKind, object), "relation-link");
        button.onclick = () => {
          const current = this.renderer.resolve({ kind: relationKind, id: object.id });
          if (current) this.pick(relationKind, current);
        };
        row.append(button);
        return row;
      };
      related.append(
        relationSection("blocks", blockItems, "noRelatedBlocks", (block) => {
          const row = relationButton("blocks", block);
          const badges = el("div", undefined, "relation-badges");
          badges.append(el("span", t(block.occupied ? "occupied" : "free"), "status-badge " + (block.occupied ? "occupied" : "free")));
          if (block.reserved) badges.append(el("span", t("reserved"), "status-badge reserved"));
          row.append(badges);
          return row;
        }),
        relationSection("routes", routeItems, "noRelatedRoutes", (route) => {
          const row = relationButton("routes", route);
          const badges = el("div", undefined, "relation-badges");
          badges.append(el("span", t(route.status || route.lifecycle || "planned"), "status-badge route-status"));
          if (route.reservationState && route.reservationState !== "none")
            badges.append(el("span", t(route.reservationState), "status-badge reserved"));
          row.append(badges);
          return row;
        }),
      );
      root.append(related);
      this.kv(
        root,
        "occupation",
        this.store.occupancy.has(id)
          ? t(this.store.occupancy.get(id).occupied ? "occupied" : "free")
          : t("unknown"),
      );
      this.button(actions, "startRoute", () => {
        this.startTrain = null;
        this.startTrack = id;
        this.endTrack = null;
        this.routeVia = [];
        this.routeWaypointPicking = false;
        this.store.preview = null;
        this.renderRoute();
      });
      this.button(actions, "endRoute", () => {
        this.endTrack = id;
        this.previewRoute();
      });
      if (this.startTrack && this.endTrack && id !== this.startTrack && id !== this.endTrack)
        this.button(actions, "addWaypoint", () => this.addWaypoint(id));
    }
    if (kind === "cars" || kind === "trains" || kind === "wagonGroups") {
      if (kind === "trains") this.kv(root, "type", t(trainDescription(item)));
      this.kv(root, "speed", number(Math.abs(item.speed), 1) + " " + t("kmh"));
      this.kv(root, "length", number(item.length, 1) + " " + t("meters"));
      this.kv(
        root,
        "direction",
        t(
          !Number.isFinite(item.direction)
            ? "unknown"
            : item.direction === 0
              ? "stopped"
              : item.direction > 0
                ? "alongTrack"
                : "againstTrack",
        ),
      );
      this.kv(root, "track", trackName(this.store, item.track1));
      const assignedJob = primaryJob(
        this.store,
        kind === "trains" ? this.store.cars.get(item.head) || item : item,
      );
      if (kind === "cars" && !item.locomotive)
        this.kv(root, "job", assignedJob?.id);
      this.kv(
        root,
        "destination",
        assignedJob?.destination
          ? locationName(this.store, assignedJob.destination)
          : "—",
      );
      if (item.derailed) root.append(el("p", t("derailed"), "warning"));
      if (item.slipping) root.append(el("p", t("slipping"), "warning"));
      this.button(actions, "follow", () => {
        this.renderer.follow = this.selected;
        $("follow").classList.add("active");
      });
      if (kind === "trains" || kind === "wagonGroups")
        this.button(actions, "showCars", () => {
          this.setTab("cars", { query: item.id });
        });
      const car = kind === "trains" ? this.store.cars.get(item.head) : item;
      if (
        car?.controllable &&
        this.user?.role === "admin" &&
        this.store.capabilities.locoControls
      )
        this.loco(root, car);
      const route = this.store.routes.find((r) =>
        r.tracks.includes(item.track1),
      );
      if (route && !route.endedAt && Math.abs(item.speed) > 1) {
        const at = route.tracks.indexOf(item.track1),
          direction = route.directions?.[at];
        if (direction && direction === item.direction) {
          let remaining =
            direction > 0
              ? (this.store.tracks.get(item.track1)?.length || 0) - item.span1
              : item.span1;
          for (let i = at + 1; i < route.tracks.length; i++)
            remaining += this.store.tracks.get(route.tracks[i])?.length || 0;
          this.kv(
            root,
            "eta",
            "≈ " +
              number(Math.max(0, remaining) / (Math.abs(item.speed) / 3.6)) +
              " " +
              t("seconds"),
          );
        }
      }
    }
    if (kind === "switches") {
      const protectedRoute = this.store.routes.find(
        (r) =>
          !r.endedAt &&
          r.reservationMode === "protected" &&
          r.reservationState === "reserved" &&
          r.switches?.some((s) => s.id === id),
      );
      if (protectedRoute) {
        root.append(el("p", t("SWITCH_LOCKED"), "warning"));
        const link = el(
          "button",
          entityName(this.store, "routes", protectedRoute),
        );
        link.onclick = () => this.pick("routes", protectedRoute);
        root.append(link);
      }
      this.kv(
        root,
        "branch",
        this.store.stale || !Number.isInteger(item.branch) || item.branch < 0
          ? t("unknown")
          : item.branch + 1,
      );
      const incoming = [item.incoming, ...(item.branches || [])];
      if (incoming.some((track) => this.store.occupancy.get(track)?.occupied))
        root.append(el("p", t("TRACK_OCCUPIED"), "warning"));
      const approaching = [...this.store.cars.values()].some(
        (c) =>
          Math.abs(c.speed) > 1 &&
          (incoming.includes(c.track1) || incoming.includes(c.track2)) &&
          Math.hypot(c.x - item.x, c.z - item.z) < 150,
      );
      if (approaching) root.append(el("p", t("APPROACHING_TRAIN"), "warning"));
      for (let b = 0; b < (item.branches?.length || 0); b++) {
        const button = this.button(
          actions,
          "branch",
          () =>
            this.command("setSwitch", {
              target: id,
              branch: b,
              expectedRevision: item.revision,
            }),
          true,
          !protectedRoute || b === item.branch,
        );
        button.textContent = t("branch") + " " + (b + 1);
        button.classList.toggle("primary", b === item.branch);
      }
    }
    if (kind === "turntables") {
      this.kv(root, "tableAngle", number(item.angle, 1) + "°");
      this.kv(
        root,
        "status",
        t(
          item.moving
            ? "tableMoving"
            : item.available
              ? "ready"
              : item.reason || "unknown",
        ),
      );
      this.kv(
        root,
        "connectedTracks",
        [item.front, item.rear]
          .map((id) => trackName(this.store, id))
          .join(" ↔ "),
      );
      root.append(el("p", t("tableSafetyHint"), "warning"));
      for (let i = 0; i < (item.ends?.length || 0); i++) {
        const end = item.ends[i];
        const b = this.button(
          actions,
          "setTurntable",
          () =>
            this.command("setTurntable", {
              target: id,
              index: i,
              expectedRevision: item.revision,
            }),
          true,
          item.available === true,
        );
        b.dataset.key = "table-end-" + i;
        b.textContent =
          trackName(this.store, end.track) + " · " + number(end.angle, 1) + "°";
      }
    }
    if (kind === "signals") renderSignalControls(this, root, actions, item);
    if (kind === "blocks") {
      this.kv(
        root,
        "source",
        item.source === "dispatch" ? t("section") : item.source,
      );
      this.kv(
        root,
        "occupation",
        item.quality === "unknown"
          ? t("unknown")
          : t(item.occupied ? "occupied" : "free"),
      );
      this.kv(root, "reservation", t(item.reserved ? "reserved" : "none"));
      this.kv(root, "length", number(item.length) + " " + t("meters"));
      this.kv(root, "tracks", item.tracks?.length);
      this.kv(
        root,
        "trains",
        item.trains
          ?.map((id) => entityName(this.store, "trains", id))
          .join(", ") || "—",
      );
    }
    if (kind === "routes") {
      routeDetails(this, root, item);
      reservationActions(this, actions, item);
    }
    if (kind === "jobs") jobDetails(this, root, item);
    if (kind === "log") renderEvent(this, root, actions, item);
    if (kind === "players") {
      this.kv(root, "role", t(item.host ? "host" : "client"));
      this.kv(root, "cars", entityName(this.store, "cars", item.car));
      this.button(
        actions,
        "follow",
        () => (this.renderer.follow = this.selected),
      );
    }
    extraInspector(this, root, actions, kind, item);
    if (actions.childElementCount) root.append(actions);
    const technical = el("details", undefined, "technical-details");
    technical.dataset.key = "technical-details";
    technical.append(el("summary", t("technicalDetails")), el("code", id));
    if (kind === "signals")
      technical.append(el("p", item.aspect), el("p", item.mode));
    if (kind === "log")
      for (const key of [
        "code",
        "detail",
        "target",
        "targetKind",
        "actorId",
        "source",
        "time",
      ])
        if (item[key] !== undefined && item[key] !== null)
          technical.append(el("p", key + ": " + item[key]));
    root.append(technical);
    const selection = kind + ":" + id;
    if (target.dataset.selection !== selection) target.replaceChildren();
    target.dataset.selection = selection;
    syncChildren(target, root);
    tickClocks(this);
  }
  loco(root, car) {
    this.kv(root, "brakePipe", number(car.brakePipe, 1) + " bar");
    for (const control of [
      "throttle",
      "trainBrake",
      "independentBrake",
      "reverser",
    ]) {
      const row = el("label", t(control)),
        input = el("input");
      input.type = "range";
      row.dataset.key = "loco-" + control;
      input.min = 0;
      input.max = 100;
      input.step = control === "reverser" ? 50 : 1;
      input.value = Math.round(car[control] * 100);
      input.dataset.mutating = "true";
      input.dataset.admin = "true";
      input.disabled = !this.store.canControl || this.user?.role === "viewer";
      input.onchange = (e) =>
        this.command("loco", {
          target: car.id,
          action: control,
          value: Number(e.currentTarget.value) / 100,
        });
      row.append(input);
      root.append(row);
    }
    const actions = el("div", undefined, "actions");
    actions.dataset.key = "loco-couplers";
    if (car.canCouple === true)
      this.button(
        actions,
        "couple",
        () => this.command("loco", { target: car.id, action: "couple" }),
        true,
      );
    const select = el("select");
    select.dataset.key = "uncouple-index";
    select.dataset.preserveValue = "true";
    for (let i = car.frontCount; i >= -car.rearCount; i--) {
      if (i === 0) continue;
      const o = el("option", i > 0 ? "+" + i : String(i));
      o.value = i;
      select.append(o);
    }
    this.button(
      actions,
      "uncouple",
      () =>
        this.command("loco", {
          target: car.id,
          action: "uncouple",
          index: Number(
            $("inspector").querySelector('[data-key="uncouple-index"]').value,
          ),
        }),
      true,
      select.options.length > 0,
    );
    root.append(actions, select);
  }
  addWaypoint(id) {
    if (!id || !this.store.tracks.has(id) || !this.startTrack || !this.endTrack) return;
    if (id === this.startTrack || id === this.endTrack || this.routeVia.includes(id)) {
      this.toast(t("routeWaypointInvalid"), true);
      this.renderRoute();
      return;
    }
    this.routeVia = [...this.routeVia, id];
    this.store.preview = null;
    this.renderer.overlayDirty = true;
    this.renderRoute();
    this.previewRoute();
  }
  removeWaypoint(index) {
    if (index < 0 || index >= this.routeVia.length) return;
    this.routeVia = this.routeVia.filter((_, i) => i !== index);
    this.store.preview = null;
    this.renderRoute();
    this.previewRoute();
  }
  moveWaypoint(index, delta) {
    const target = index + delta;
    if (index < 0 || target < 0 || target >= this.routeVia.length) return;
    const next = [...this.routeVia];
    [next[index], next[target]] = [next[target], next[index]];
    this.routeVia = next;
    this.store.preview = null;
    this.renderRoute();
    this.previewRoute();
  }
  previewStateKey() {
    return JSON.stringify([
      this.startTrack,
      this.endTrack,
      this.routeVia,
      this.startTrain,
      this.store.epoch,
      this.store.topology?.epoch,
      this.store.topology?.revision,
      this.store.disconnected,
      this.store.capabilities.status,
      [...this.store.signals.values()].map((s) => [
        s.id,
        s.track,
        s.direction,
        s.span,
        s.stop,
        s.off,
        s.routeIncoming,
        s.routeBranches,
      ]),
      [...this.store.switches.values()].map((s) => [s.id, s.branch]),
      [...this.store.blocks.values()].map((b) => [
        b.id,
        b.tracks,
        b.directions,
        b.extraTracks,
        b.occupied,
        b.reserved,
      ]),
      [...this.store.occupancy.values()].map((o) => [o.id, o.occupied]),
      this.store.routes.map((r) => [
        r.id,
        r.tracks,
        r.endedAt,
        r.reservationState,
      ]),
      this.startTrain
        ? [...this.store.cars.values()]
            .filter(
              (c) => c.consist === this.startTrain || c.id === this.startTrain,
            )
            .map((c) => [c.id, c.track1, c.direction])
        : null,
      [...this.store.turntables.values()].map((s) => [
        s.id,
        s.front,
        s.rear,
        s.moving,
      ]),
    ]);
  }
  previewAffected(kind, p) {
    if (
      ["topology", "snapshot", "routes", "connection", "capabilities"].includes(
        kind,
      )
    )
      return true;
    if (kind !== "delta") return false;
    return !!(
      p.reset ||
      p.capabilities ||
      p.signals?.length ||
      p.removedSignals?.length ||
      p.switches?.length ||
      p.blocks?.length ||
      p.removedBlocks?.length ||
      p.occupancy?.length ||
      p.turntables?.length ||
      (this.startTrain &&
        (p.cars?.length || p.motions?.length || p.removedCars?.length))
    );
  }
  clearRoutePreview(retainHistory = false) {
    if (!retainHistory) this.lastPreview = null;
    this.routeRequest = (this.routeRequest || 0) + 1;
    this.routeAbort?.abort();
    this.routeAbort = null;
    this.previewPending = false;
    this.previewKey = null;
    this.planningError = null;
    this.store.preview = null;
    this.renderer.overlayDirty = true;
  }
  async previewRoute() {
    this.clearRoutePreview(true);
    if (!this.startTrack || !this.endTrack || this.store.disconnected) {
      this.renderRoute();
      return;
    }
    const request = this.routeRequest,
      key = this.previewStateKey();
    this.previewKey = key;
    this.previewPending = true;
    this.routeAbort = new AbortController();
    this.renderRoute();
    try {
      const route = await this.network.route(
        this.startTrack,
        this.endTrack,
        this.startTrain || "",
        this.routeAbort.signal,
        this.routeVia,
      );
      if (request !== this.routeRequest) return;
      if (key !== this.previewStateKey()) return this.previewRoute();
      const epoch =
        this.store.topology?.epoch + ":" + this.store.topology?.revision;
      this.store.preview = previewWarnings(
        this.previewEpoch === epoch ? this.lastPreview : null,
        route,
      );
      this.previewEpoch = epoch;
      this.lastPreview = this.store.preview;
      this.renderer.overlayDirty = true;
      this.renderRoute();
    } catch (e) {
      if (request === this.routeRequest && e.name !== "AbortError") {
        this.previewKey = null;
        this.toast(t(e.message), true);
        this.renderRoute();
      }
    } finally {
      if (request === this.routeRequest) this.previewPending = false;
    }
  }
  planningContext() {
    return JSON.stringify([
      this.store.epoch,
      this.store.topology?.epoch,
      this.store.topology?.revision,
      this.startTrack,
      this.endTrack,
      this.startTrain || "",
      this.routeVia,
    ]);
  }
  async planPreview() {
    if (
      this.planning ||
      this.plannedRoute ||
      !this.startTrack ||
      !this.endTrack ||
      !this.store.preview
    )
      return;
    const context = this.planningContext();
    this.planning = true;
    this.planningError = null;
    this.planningPreview = this.store.preview;
    this.renderRoute();
    try {
      const result = await this.command("planRoute", {
        from: this.startTrack,
        to: this.endTrack,
        train: this.startTrain || null,
        via: [...this.routeVia],
      });
      if (result.status !== "applied" || !result.target) {
        this.planningError = result.code || "COMMAND_FAILED";
        this.renderRoute();
        return;
      }
      if (context !== this.planningContext())
        return;
      this.plannedRoute = { id: result.target, context };
      // The final receipt carries the server's confirmed route, closing the
      // receipt/routes-stream race without choosing by name or creating a plan.
      const known = this.store.routes.find((r) => r.id === result.target);
      if (
        result.route?.id === result.target &&
        (!known || known.lifecycle === "preparing") &&
        Array.isArray(result.route.tracks)
      ) {
        this.store.routes = known
          ? this.store.routes.map((r) => (r === known ? result.route : r))
          : [...this.store.routes, result.route];
      }
      this.selectPlannedRoute();
    } finally {
      this.planning = false;
      this.planningPreview = null;
      this.renderRoute();
    }
  }
  selectPlannedRoute() {
    const pending = this.plannedRoute;
    if (!pending) return;
    if (!pending.closed && pending.context !== this.planningContext()) {
      this.plannedRoute = null;
      return;
    }
    const route = this.store.routes.find((r) => r.id === pending.id);
    if (!route) {
      // The receipt is authoritative even when the routes stream is one frame
      // behind. Close the draft immediately and keep the exact ID pending;
      // the next routes snapshot/delta will open its inspector.
      if (!pending.closed) this.closePlanningDraft();
      return;
    }
    if (route.lifecycle === "preparing") return;
    if (route.endedAt || route.lifecycle === "failed") {
      this.plannedRoute = null;
      this.toast(t(route.reason || "ROUTE_ENDED"), true);
      this.renderRoute();
      return;
    }
    this.plannedRoute = null;
    this.closePlanningDraft();
    this.pick("routes", route);
    const index = this.items.findIndex((r) => r.id === route.id);
    if (index >= 0) {
      $("object-list").scrollTop = index * rowHeight();
      this.renderRows();
    }
  }
  closePlanningDraft() {
    if (this.plannedRoute) {
      this.plannedRoute.closed = true;
      this.plannedRoute.context = null;
    }
    this.routeRequest = (this.routeRequest || 0) + 1;
    this.routeAbort?.abort();
    this.startTrack = this.endTrack = this.startTrain = null;
    this.routeVia = [];
    this.routeWaypointPicking = false;
    this.store.preview = null;
    this.tabFilters.delete("routes");
    this.tabQueries.delete("routes");
    this.setTab("routes", { query: "" });
    $("status-filter").value = "activeRoutes";
    $("active-only").checked = false;
    this.collect();
    this.renderRows();
    this.renderRoute();
    this.renderer.overlayDirty = true;
  }
  renderRoute() {
    if (this.plannedRoute && !this.plannedRoute.closed && this.plannedRoute.context !== this.planningContext())
      this.plannedRoute = null;
    this.renderer.trackPicking =
      this.tab === "tracks" || !!this.startTrack || this.routeWaypointPicking;
    const target = $("route-bar"),
      root = el("div");
    if (target.hidden !== !this.startTrack) target.hidden = !this.startTrack;
    if (!this.startTrack || !this.endTrack) this.clearRoutePreview();
    if (!this.startTrack) {
      target.replaceChildren();
      return;
    }
    const from = this.store.tracks.get(this.startTrack),
      train = this.store.train(this.startTrain);
    root.append(
      el(
        "strong",
        train
          ? entityName(this.store, "trains", train)
          : trackName(this.store, from || this.startTrack),
      ),
      el("span", "→"),
    );
    const destination = el("input"),
      options = el("datalist");
    options.id = "route-destinations";
    destination.setAttribute("list", options.id);
    destination.placeholder = t("selectEnd");
    destination.setAttribute("aria-label", t("destination"));
    destination.value = this.endTrack
      ? trackName(this.store, this.endTrack)
      : "";
    destination.dataset.key = "route-destination";
    const suggest = (input = destination, output = options) => {
      output.replaceChildren();
      const query = normalizeSearch(input.value);
      for (const track of [...this.store.tracks.values()]
        .filter((x) => matchesSearch(this.store, "tracks", x, query))
        .slice(0, 60)) {
        const option = el("option");
        option.value = trackName(this.store, track);
        output.append(option);
      }
    };
    destination.oninput = (e) => {
      const input = e.currentTarget;
      // Editing the destination invalidates its previous route immediately.
      if (
        this.endTrack &&
        input.value !== trackName(this.store, this.endTrack)
      ) {
        this.endTrack = null;
        this.clearRoutePreview();
        for (const node of target.querySelectorAll(
          ".route-preview-details,[data-key='action-plan']",
        ))
          node.remove();
      }
      suggest(input, document.getElementById("route-destinations"));
    };
    suggest();
    root.append(destination, options);
    const waypoints = el("div", undefined, "route-waypoints");
    waypoints.dataset.key = "route-waypoints";
    if (this.routeVia.length) {
      waypoints.append(el("strong", t("routeWaypoints")));
      const list = el("ol");
      this.routeVia.forEach((id, index) => {
        const row = el("li", undefined, "route-waypoint");
        row.dataset.id = id;
        row.append(el("span", trackName(this.store, id)));
        const actions = el("span", undefined, "route-waypoint-actions");
        const up = el("button", "↑");
        up.type = "button";
        up.title = t("moveWaypointUp");
        up.disabled = index === 0;
        up.onclick = () => this.moveWaypoint(index, -1);
        const down = el("button", "↓");
        down.type = "button";
        down.title = t("moveWaypointDown");
        down.disabled = index === this.routeVia.length - 1;
        down.onclick = () => this.moveWaypoint(index, 1);
        const remove = el("button", "×");
        remove.type = "button";
        remove.title = t("removeWaypoint");
        remove.onclick = () => this.removeWaypoint(index);
        actions.append(up, down, remove);
        row.append(actions);
        list.append(row);
      });
      waypoints.append(list);
    }
    const addWaypoint = this.button(
      root,
      this.routeWaypointPicking ? "choosingWaypoint" : "addWaypoint",
      () => {
        this.routeWaypointPicking = !this.routeWaypointPicking;
        this.renderRoute();
      },
      false,
      !!this.endTrack && !this.planning && !this.plannedRoute,
    );
    addWaypoint.dataset.key = "action-add-waypoint";
    if (this.routeVia.length) {
      const clearWaypoints = this.button(root, "clearWaypoints", () => {
        this.routeVia = [];
        this.store.preview = null;
        this.renderRoute();
        this.previewRoute();
      });
      clearWaypoints.dataset.key = "action-clear-waypoints";
    }
    root.append(waypoints);
    this.button(root, "calculateRoute", () => {
      const value = target.querySelector(
        '[data-key="route-destination"]',
      ).value;
      const normalized = normalizeSearch(value);
      const matches = normalized
        ? [...this.store.tracks.values()].filter((track) =>
            [track.id, track.name, trackName(this.store, track)].some(
              (name) => normalizeSearch(name) === normalized,
            ),
          )
        : [];
      if (matches.length !== 1) {
        this.toast(t("selectValidTrack"), true);
        return;
      }
      this.endTrack = matches[0].id;
      this.previewRoute();
    });
    if (this.store.preview || this.planning || this.plannedRoute) {
      const preview =
        this.store.preview || this.planningPreview || this.lastPreview;
      if (preview) {
        root.append(el("span", number(preview.length) + " " + t("meters")));
        const planButton = this.button(
          root,
          "plan",
          () => this.planPreview(),
          true,
          !this.planning && !this.plannedRoute && !!this.store.preview,
        );
        if (this.planning || this.plannedRoute)
          planButton.textContent = t(
            this.planning ? "planningRoute" : "waitingRoute",
          );
        if (this.planningError)
          root.append(
            el(
              "p",
              localizedValue(this.planningError, "COMMAND_FAILED"),
              "warning route-planning-error",
            ),
          );
        const details = el("details", undefined, "route-preview-details"),
          summary = el(
            "summary",
            t("routeDetails") +
              " · " +
              t("warnings") +
              ": " +
              number(preview.conflicts.length),
          );
        details.append(summary);
        details.dataset.key = "route-preview-details";
        details.dataset.controlledOpen = "true";
        details.open = !!this.routePreviewOpen;
        details.ontoggle = (e) => {
          this.routePreviewOpen = e.currentTarget.open;
        };
        routeDetails(this, details, preview, "preview");
        root.append(details);
      }
    }
    this.button(root, "clear", () => {
      this.routeRequest = (this.routeRequest || 0) + 1;
      this.startTrack = this.endTrack = this.startTrain = null;
      this.routeVia = [];
      this.routeWaypointPicking = false;
      this.store.preview = null;
      target.hidden = true;
      this.renderer.overlayDirty = true;
    });
    syncChildren(target, root);
  }
  settings() {
    const root = $("settings-panel");
    root.replaceChildren();
    const groups = displaySettings(this, root);
    this.button(groups.interfaceSettings, "resetPanels", () =>
      window.dispatchEvent(new Event("ads-reset-panels")),
    );
    groups.interfaceSettings.append(el("p", t("resizeHint")));
    groups.dispatchSettings.append(
      el("h4", t("advisoryShort")),
      el("p", t("advisory")),
      el("p", t("settingsRestart")),
    );
    const color = el("select");
    for (const [key, label] of [
      ["jobType", "colorJobType"],
      ["destination", "colorDestination"],
    ]) {
      const option = el("option", t(label));
      option.value = key;
      color.append(option);
    }
    color.value = this.renderer.colorMode;
    color.onchange = () => {
      this.renderer.colorMode = color.value;
      saveSetting("ads.colors", color.value);
      const legend = root.querySelector('[data-key="car-color-legend"]');
      if (legend) syncChildren(legend, colorLegend(this.store, color.value));
    };
    const label = el("label", t("colorMode"));
    label.append(color);
    groups.mapSettings.append(
      label,
      colorLegend(this.store, this.renderer.colorMode),
    );
    this.button(groups.dispatchSettings, "exportLog", () => {
      const blob = new Blob([JSON.stringify(this.store.events, null, 2)], {
          type: "application/json",
        }),
        a = el("a");
      a.href = URL.createObjectURL(blob);
      a.download = "ads-events.json";
      a.click();
      setTimeout(() => URL.revokeObjectURL(a.href), 500);
    });
    const access = groups.accessSettings;
    access.append(
      el("p", t("webAccessExplanation")),
      el(
        "p",
        t(
          this.user?.role === "admin"
            ? "ownerAccessHelp"
            : this.user?.role === "dispatcher"
              ? "dispatcherAccessHelp"
              : "viewerAccessHelp",
        ),
      ),
    );
    if (this.user?.role !== "admin") return;
    access.append(el("h4", t("accounts")), el("p", t("accountHint")));
    const existing = el("select"),
      existingLabel = el("label", t("existingAccounts"));
    existing.id = "existing-accounts";
    existingLabel.append(existing);
    access.append(existingLabel);
    const loadAccounts = async () => {
      existing.replaceChildren(el("option", t("loading")));
      try {
        const accounts = await this.network.accounts();
        if (!access.isConnected) return;
        existing.replaceChildren(el("option", t("newAccount")));
        existing.firstChild.value = "";
        for (const account of accounts) {
          const option = el("option", account.name + " · " + t(account.role));
          option.value = account.name;
          option.dataset.role = account.role;
          existing.append(option);
        }
      } catch (error) {
        existing.replaceChildren(el("option", t("accountsUnavailable")));
        console.warn("ADS_ACCOUNTS_READ_FAILED", error.name);
      }
    };
    loadAccounts();
    const form = el("form"),
      name = el("input"),
      password = el("input"),
      role = el("select");
    form.id = "account-management-form";
    name.autocomplete = "off";
    existing.onchange = () => {
      name.value = existing.value;
      password.value = "";
      role.value = existing.selectedOptions?.[0]?.dataset.role || "viewer";
    };
    name.required = true;
    name.maxLength = 48;
    password.type = "password";
    password.minLength = 10;
    password.maxLength = 128;
    password.required = true;
    password.autocomplete = "new-password";
    for (const value of ["viewer", "dispatcher"]) {
      const option = el("option", t(value));
      option.value = value;
      role.append(option);
    }
    for (const [key, input] of [
      ["username", name],
      ["password", password],
      ["role", role],
    ]) {
      const label = el("label", t(key));
      label.append(input);
      form.append(label);
    }
    const submit = el("button", t("saveAccount"), "primary");
    submit.type = "submit";
    form.append(submit);
    form.onsubmit = async (e) => {
      e.preventDefault();
      submit.disabled = true;
      try {
        const r = await fetch("/api/accounts", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({
            name: name.value,
            password: password.value,
            role: role.value,
          }),
        });
        this.toast(t(r.ok ? "accountSaved" : "accountFailed"), !r.ok);
        if (r.ok) {
          password.value = "";
          await loadAccounts();
        }
      } catch (error) {
        console.warn("ADS_ACCOUNT_SAVE_FAILED", error.name);
        this.toast(t("accountFailed"), true);
      } finally {
        submit.disabled = false;
      }
    };
    access.append(form);
    this.button(
      groups.dispatchSettings,
      "rescan",
      () => this.command("rescan"),
      true,
    );
  }
}
