import { accountSettings } from "./account-settings.js";
import { matchesSearch, normalizeSearch, tabLabel } from "./entity-search.js";
import { consistService } from "./consist-service.js";
import { pointsFocus } from "./object-focus.js";
import { mapViewport } from "./map-viewport.js";
import { wagonRows, wagonGroupSummary } from "./wagon-groups.js";
import {
  isWagon,
  isLocomotive,
  trainDescription,
  vehicleHeading,
} from "./rolling-stock.js";
import { routeTime } from "./route-time.js";
import { duration } from "./operations.js";
import { previewWarnings } from "./route-preview.js";
import { uiIcon, locomotiveIcon } from "./ui-icons.js";
import { groupedJobs } from "./job-groups.js";
import { renderEvent } from "./event-details.js";
import { jobIcon } from "./job-icons.js";
import { isJobAction, jobEventContent } from "./job-events.js";
import { renderSignalControls } from "./signal-controls.js";
import { countText } from "./localization.js";
import { isSignal, railwaySigns } from "./railway-objects.js";
import { saveSetting } from "./storage.js";
import { preferences } from "./preferences.js";
import {
  jobColor,
  colorLegend,
  jobTypeText,
  jobTypeKey,
  primaryJob,
  carJobVisual,
  neutralJobColor,
  jobColorForStore,
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
  syncHostSettings,
  jobDetails,
  routeDetails,
  extraInspector,
} from "./ui-details.js";
import { passengerJob, passengerStops, passengerStopLabel } from "./job-progress.js";
import { detailsBack } from "./details-navigation.js";
import { locomotiveLoadRating } from "./locomotive-catalog.js";
import { tractionAssessment } from "./traction-assessment.js";
import { weatherState, weatherLabelKey, weatherIconKey, weatherMeasurements, formatGameTime } from "./weather.js";
export class UI {
  constructor(store, network, renderer) {
    this.store = store;
    this.network = network;
    this.renderer = renderer;
    this.tab = "trains";
    this.query = "";
    this.selected = null;
    this.navigationStack = [];
    this.items = [];
    this.dirty = true;
    this.lastList = 0;
    this.startTrack = null;
    this.endTrack = null;
    this.routeVia = [];
    this.avoidForeignReservations = true;
    this.routeJobId = null;
    this.routeTaskIndex = -1;
    this.routeTaskId = null;
    this.routeEditingId = null;
    this.requiredRouteVia = [];
    this.user = null;
    this.health = null;
    this.signalFilter = "all";
    this.tabQueries = new Map();
    this.tabFilters = new Map();
    this.pendingCommands = 0;
    this.planningError = null;
    this.autoRouteNotices = new Set();
    this.autoRouteToasts = new Map();
    this.jobProgressSeen = new Map();
    this.renderer.onSwitchClick = (item) => this.toggleSwitchFromMap(item);
    this.bind();
    setInterval(() => this.refresh(), 250);
  }
  mobileViewport() {
    return globalThis.matchMedia?.("(max-width: 720px), (max-width: 900px) and (max-height: 600px)").matches === true;
  }
  renderMobileNavigation() {
    const root = $("mobile-nav"), more = $("mobile-more-items");
    if (!root || !more) return;
    root.replaceChildren();
    more.replaceChildren();
    const add = (parent, key, labelKey = key) => {
      const button = el("button", undefined, "mobile-nav-button");
      button.type = "button";
      button.dataset.tab = key;
      const icon = el("span", undefined, "mobile-nav-icon");
      icon.append(uiIcon(key));
      button.append(icon, el("span", t(labelKey), "mobile-nav-label"));
      button.setAttribute("aria-label", t(labelKey));
      parent.append(button);
      return button;
    };
    add(root, "map", "mobileMap");
    for (const key of ["routes", "trains", "jobs"]) add(root, key);
    const moreButton = add(root, "more", "mobileMore");
    moreButton.classList.add("mobile-more-trigger");
    const primary = new Set(["routes", "trains", "jobs"]);
    const moreTabs = ["locations", "cars", "signals", "switches", "turntables", "blocks", "players", "tracks", "signs", "weather", "log", "settings"];
    for (const key of moreTabs)
      if (!primary.has(key)) add(more, key);
    for (const button of root.children)
      button.classList.toggle("active", button.dataset.tab === this.tab);
    for (const button of more.children)
      button.classList.toggle("active", button.dataset.tab === this.tab);
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
      "weather",
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
    this.renderMobileNavigation();
    // Keep the mobile controls event delegated to the stable nav element. It
    // survives language refreshes and DOM replacement of the button list.
    $("mobile-nav")?.addEventListener("click", (event) => {
      const button = event.target.closest?.("button[data-tab]");
      if (!button) return;
      const key = button.dataset.tab;
      if (key === "more") {
        const panel = $("mobile-more");
        panel.hidden = !panel.hidden;
        document.body.classList.toggle("mobile-more-open", !panel.hidden);
        if (!panel.hidden) $("mobile-more-close")?.focus();
      } else if (key === "map") {
        document.body.classList.remove("mobile-list-open", "mobile-sheet-open");
        document.body.classList.add('mobile-map-only');
        this.renderer.cancelPan?.();
        this.renderer.resize?.();
      } else this.setTab(key);
    });
    $("mobile-more-items")?.addEventListener("click", (event) => {
      const button = event.target.closest?.("button[data-tab]");
      if (!button) return;
      this.setTab(button.dataset.tab);
      $("mobile-more").hidden = true;
      document.body.classList.remove("mobile-more-open");
    });
    window.addEventListener("ads-language", () => this.renderMobileNavigation());
    $("mobile-more-close")?.addEventListener("click", () => {
      $("mobile-more").hidden = true;
      document.body.classList.remove("mobile-more-open");
    });
    const sheetToggle = $("mobile-sheet-toggle"), sheetFrame = $("inspector-frame");
    let sheetDrag = null, suppressSheetClick = false;
    const setSheetOffset = (offset) => {
      if (sheetFrame) sheetFrame.style.setProperty("--mobile-sheet-offset", `${Math.max(0, offset)}px`);
    };
    const syncSheetState = () => {
      sheetToggle?.setAttribute("aria-expanded", String(!document.body.classList.contains("mobile-sheet-collapsed")));
    };
    const endSheetDrag = (event) => {
      if (!sheetDrag || (event && event.pointerId !== sheetDrag.id)) return;
      const drag = sheetDrag;
      sheetDrag = null;
      if (sheetToggle?.hasPointerCapture?.(drag.id)) sheetToggle.releasePointerCapture(drag.id);
      sheetToggle?.classList.remove("dragging");
      setSheetOffset(0);
      if (!drag.moved) return;
      suppressSheetClick = true;
      const dy = (event?.clientY ?? drag.lastY) - drag.startY;
      if (dy > Math.max(72, (sheetFrame?.clientHeight || 300) * .18))
        document.body.classList.add("mobile-sheet-collapsed");
      else if (dy < -36)
        document.body.classList.remove("mobile-sheet-collapsed");
      syncSheetState();
    };
    sheetToggle?.addEventListener("pointerdown", (event) => {
      if (event.button !== 0 && event.pointerType !== "touch") return;
      sheetDrag = { id: event.pointerId, startY: event.clientY, lastY: event.clientY, moved: false };
      suppressSheetClick = false;
      sheetToggle.classList.add("dragging");
      sheetToggle.setPointerCapture?.(event.pointerId);
      event.preventDefault();
    });
    sheetToggle?.addEventListener("pointermove", (event) => {
      if (!sheetDrag || event.pointerId !== sheetDrag.id) return;
      const dy = event.clientY - sheetDrag.startY;
      sheetDrag.lastY = event.clientY;
      if (Math.abs(dy) <= 4) return;
      sheetDrag.moved = true;
      if (dy < -8) document.body.classList.remove("mobile-sheet-collapsed");
      setSheetOffset(Math.max(0, dy));
      event.preventDefault();
    });
    for (const eventName of ["pointerup", "pointercancel", "lostpointercapture"])
      sheetToggle?.addEventListener(eventName, endSheetDrag);
    sheetToggle?.addEventListener("click", () => {
      if (suppressSheetClick) { suppressSheetClick = false; return; }
      document.body.classList.toggle("mobile-sheet-collapsed");
      syncSheetState();
    });
    const updateMobileLabels = () => $("mobile-sheet-toggle")?.setAttribute("aria-label", t("toggleDetails"));
    updateMobileLabels();
    window.addEventListener("ads-language", updateMobileLabels);
    $("mobile-more")?.addEventListener("click", (event) => {
      if (event.target === $("mobile-more")) {
        $("mobile-more").hidden = true;
        document.body.classList.remove("mobile-more-open");
      }
    });
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
        this.store.playerPosition(p, this.renderer.frameTime),
      );
      const target=pointsFocus(this.renderer,players.map(p=>[p.x,p.z]));
      if(target) {
        const area=mapViewport(this.renderer);
        this.renderer.scale=target.zoom;
        this.renderer.center(target.x-(area.left+area.width/2-this.renderer.width/2)/target.zoom,target.z);
      }
      this.syncTrackingControls();
    };
    $("follow").onclick = () => {
      const moving=item=>["cars","trains","wagonGroups","players"].includes(item?.kind);
      const active=this.renderer.follow|| (moving(this.renderer.cameraFocus?.item)&&this.renderer.cameraFocus.item);
      this.renderer.cameraFocus=null;
      this.renderer.follow=active?null:moving(this.selected)?this.selected:null;
      this.syncTrackingControls();
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
      if (!this.detailNavigation) this.navigationStack = [];
      if (
        this.selected?.id !== e.detail?.id ||
        this.selected?.kind !== e.detail?.kind
      ) {
        this.signalAspectDraft = null;
        this.signalModeDraft = null;
      }
      this.selected = e.detail;
      if (this.mobileViewport() && this.selected) {
        document.body.classList.remove('mobile-map-only');
        document.body.classList.remove("mobile-list-open", "mobile-sheet-collapsed");
        document.body.classList.add("mobile-sheet-open");
      }
      this.syncTrackingControls();
      if (this.selected?.kind === "routes" && this.startTrack) {
        this.store.preview = null;
        this.startTrack = this.endTrack = this.startTrain = null;
        this.routeVia = [];
        this.routeJobId = null;
        this.routeTaskIndex = -1;
        this.routeTaskId = null;
        this.routeEditingId = null;
        this.requiredRouteVia = [];
        this.renderRoute();
      }
      this.renderInspector();
      this.renderRows();
      this.dirty = true;
    });
    this.renderer.addEventListener("route-focus", () => {
      this.renderInspector();
      this.layers();
    });
    this.renderer.addEventListener("tracking",()=>this.syncTrackingControls());
    $("exit-route-focus").onclick = () => this.renderer.clearRouteFocus();
    this.store.addEventListener("change", (e) => {
      const { kind, payload: p } = e.detail;
      if (this.tab === "weather" && (kind === "capabilities" || kind === "snapshot" || kind === "delta" && p.capabilities)) { this.collect(); this.renderRows(); }
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
      if (kind === "snapshot" || kind === "delta" && (p.jobs?.length || p.replaceJobs)) {
        for (const job of this.store.jobs.values()) {
          const previous = this.jobProgressSeen.get(job.id);
          const current = (job.legs || []).map(leg => leg.progress || "unknown");
          if (previous && passengerJob(job)) {
            for (let i = 0; i < current.length; i++) {
              if (previous[i] === "completed" || current[i] !== "completed") continue;
              const leg = job.legs[i];
              if (!leg?.passengerStop) continue;
              const stop = passengerStops(job).find(item => item.legs.includes(leg));
              const label = stop ? passengerStopLabel(this.store, stop) : leg.station || leg.toTrack;
              const next = passengerStops(job).find(item => item.progress === "active" || item.progress === "pending");
              let message = t("passengerStopCompleted").replace("{stop}", label);
              if (next) message += " " + t("passengerNextStop").replace("{stop}", passengerStopLabel(this.store, next));
              this.toast(message);
            }
          }
          this.jobProgressSeen.set(job.id, current);
        }
        const selectedJob = this.selected?.kind === "jobs"
          ? this.store.jobs.get(this.selected.id)
          : null;
        if (
          this.selected?.kind === "jobs" &&
          !selectedJob
        )
          this.renderer.select(null);
      }
      if (kind === "routes" || kind === "snapshot") this.handleAutomaticRouteUpdate();
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
        this.routeJobId = null;
        this.routeTaskIndex = -1;
        this.routeTaskId = null;
        this.routeEditingId = null;
        this.requiredRouteVia = [];
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
        // Keep the last confirmed preview visible while the replacement
        // request is in flight. Clearing it here caused a one-frame blank
        // route on every signal/block delta (most noticeable with shunting
        // heads, which can update independently of the route reservation).
        // Let the in-flight request notice the changed key and chain one
        // replacement request; restarting it for every frame defeats that
        // coalescing and creates its own visible churn.
        if (this.previewPending && !this.store.disconnected) {
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
    this.setTab("trains", { mobileInitial: true });
  }
  handleAutomaticRouteUpdate() {
    const activeNotices = new Set(), notifyingRoutes = new Set();
    const pathKey = (path) => JSON.stringify([
      path?.tracks || [], path?.directions || [],
      (path?.switches || []).map(s => [s.id, s.branch]).sort((a,b)=>String(a[0]).localeCompare(String(b[0]))),
      (path?.turntables || []).map(s => [s.id,s.from,s.to,s.fromEnd,s.toEnd,s.position]),
    ]);
    const attention = this.autoRouteAttention ||= new Map();
    for (const route of this.store.routes || []) {
      if (route.endedAt) continue;
      const state = route.recalculationState;
      const stageAvailable = route.staged && route.stageStatus === "available";
      const failure = state === "failed" || state === "unconfirmed";
      if (!failure && !stageAvailable && !["available", "recalculating", "preview", "replacing"].includes(state)) continue;
      const affectedTrack = route.editPreview?.affectedTrack || route.conflicts?.find((c) => c.track)?.track || (stageAvailable ? route.activeTo : null);
      const messageKey = stageAvailable ? "stagedLegAvailable" : failure ? route.reason || "ROUTE_RECALCULATION_FAILED"
        : state === "available" ? "AUTO_ROUTE_RECALCULATION_AVAILABLE"
        : state === "preview" ? "AUTO_ROUTE_RECALCULATING"
        : state === "replacing" ? "confirmingRouteEdit" : "AUTO_ROUTE_RECALCULATION_STARTED";
      const key = JSON.stringify([route.id, state, route.editPreview?.id, route.reason, route.invalidationReason, affectedTrack]);
      activeNotices.add(key);
      notifyingRoutes.add(route.id);
      const message = [t(messageKey), entityName(this.store, "routes", route),
        !failure && route.invalidationReason ? t(route.invalidationReason) : null,
        affectedTrack ? trackName(this.store, affectedTrack) : null,
        stageAvailable && route.activeFrom && route.activeTo ? `${trackName(this.store, route.activeFrom)} → ${trackName(this.store, route.activeTo)}` : null].filter(Boolean).join(" · ");
      const previous = this.autoRouteToasts.get(route.id);
      if (previous?.isConnected) {
        if (previous.textContent !== message) previous.textContent = message;
        previous.classList.toggle("error", failure);
      } else if (!this.autoRouteNotices.has(key)) {
        this.autoRouteToasts.set(route.id, this.toast(message, failure));
      }
      // A signal/reservation/occupancy update can change the warning track or
      // timestamp while the planned geometry remains identical.  Such a
      // state refresh must never move the operator's camera.  Focus only when
      // a staged leg becomes available or a recalculation preview contains a
      // genuinely different oriented path.
      const geometryChanged = state === "preview" && route.editPreview?.tracks?.length &&
        pathKey(route) !== pathKey(route.editPreview);
      const attentionKey = stageAvailable ? JSON.stringify(["stage", route.stageIndex, route.activeFrom, route.activeTo])
        : geometryChanged ? pathKey(route.editPreview) : null;
      if (attentionKey && affectedTrack && attention.get(route.id) !== attentionKey) {
        const points = this.store.tracks.get(affectedTrack)?.points;
        if (points?.length >= 4) {
          let x = 0, z = 0;
          for (let i = 0; i < points.length; i += 2) { x += points[i]; z += points[i + 1]; }
          this.renderer.center(x / (points.length / 2), z / (points.length / 2));
          attention.set(route.id, attentionKey);
        }
      }
    }
    this.autoRouteNotices = activeNotices;
    const liveRoutes = new Set((this.store.routes || []).filter(r => !r.endedAt).map(r => r.id));
    for (const id of attention.keys()) if (!liveRoutes.has(id)) attention.delete(id);
    // Both failure and preview notices belong to the route's current state.
    // Retire their actual DOM nodes immediately after authoritative recovery.
    for (const [id, node] of this.autoRouteToasts) if (!notifyingRoutes.has(id)) {
      node?.remove?.();
      this.autoRouteToasts.delete(id);
    }
  }
  connection(status) {
    $("connection").className =
      "connection " + (status === "connected" ? "live" : "offline");
    $("connection").lastElementChild.textContent = t(status);
    const retry = $("connection-retry");
    if (retry) retry.hidden = status !== "reconnectFailed";
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
    if (this.tab === "weather" && tab !== "weather") {
      // A collapsed tablet/mobile list can have a zero-height viewport, so
      // virtual-list rendering deliberately waits. Dispose Weather on the
      // navigation transition itself instead of waiting for that rendering.
      $("object-list").replaceChildren();
      this.listSurface=null;this.listWindow=null;this.rowRender=null;
    }
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
    document.body.classList.remove("collapsed");
    if (this.mobileViewport() && !options.mobileInitial) {
      document.body.classList.remove('mobile-map-only');
      document.body.classList.add("mobile-list-open");
      document.body.classList.remove("mobile-sheet-open", "mobile-sheet-collapsed");
    }
    $("panel-heading").textContent = t(tabLabel(tab));
    $("panel-heading").removeAttribute("data-i18n");
    if (changed) $("active-only").checked = false;
    $("object-list").scrollTop = 0;
    for (const b of $("tabs").children) {
      b.classList.toggle("active", b.dataset.tab === tab);
      b.setAttribute("aria-current", b.dataset.tab === tab ? "page" : "false");
    }
    this.renderMobileNavigation();
    $("settings-panel").hidden = tab !== "settings";
    $("list-controls").hidden = tab === "settings" || tab === "weather";
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
    document.querySelector(".list-heading").hidden = tab === "settings" || tab === "weather";
    document.querySelector(".search").hidden = tab === "settings" || tab === "weather";
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
    this.syncTrackingControls();
    if (this.tab === "settings") {
      // The host section can appear/disappear independently of personal
      // settings. Preserve active profile drafts and reset confirmations.
      syncHostSettings(this, $("settings-panel"));
    }
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
    if (this.network.lastError) text = t(this.network.lastError) + " " + t(this.network.exhausted ? "connectionRetriesStopped" : "connectionRetrying");
    else if (this.health?.accountsRecovery) text = t("accountRecovery");
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
  syncTrackingControls() {
    const moving=item=>["cars","trains","wagonGroups","players"].includes(item?.kind);
    const active=!!this.renderer.follow||moving(this.renderer.cameraFocus?.item);
    const button=$("follow");
    button.classList.toggle("active",active);
    button.setAttribute("aria-pressed",String(active));
    button.disabled=!active&&!moving(this.selected);
    const label=t(active?"stopFollowing":"follow");
    if(button.textContent!==label)button.textContent=label;
    button.title=t(active?"stopFollowing":"followSelection");
    $("home-player").disabled=this.store.players.size===0;
  }
  collect() {
    const s = this.store;
    let result;
    if (this.tab === "weather") result = s.capabilities?.weather ? [{ ...s.capabilities.weather, id: "weather", name: t("currentWeather") }] : [];
    else if (this.tab === "locations") result = allLocations(s);
    else if (this.tab === "signs") result = railwaySigns(s);
    else if (this.tab === "signals")
      result = [...s.signals.values()].filter(isSignal);
    else if (this.tab === "cars") result = [...s.cars.values()].filter(isWagon);
    else if (this.tab === "trains")
      result = [...s.cars.values()].filter(car => isLocomotive(car) || !!car.catalogModel);
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
    // Weather and Locations replace the virtualized list surface with their
    // own detail/list DOM. Drop the detached surface references when leaving
    // either view so the next ordinary tab cannot render into an old node
    // while leaving the previous panel visible.
    if (this.tab === "weather" || this.tab === "locations") {
      this.listSurface = null;
      this.listWindow = null;
      this.rowRender = null;
    }
    if (this.tab === "weather") {
      const root = $("object-list");
      root.hidden = false;
      const weather = this.store.capabilities?.weather || {};
      const panel = el("section", undefined, "weather-panel");
      panel.dataset.key = "weather-panel";
      const state = weatherState(weather);
      const icon = el("div", undefined, "weather-icon weather-" + state);
      icon.dataset.key = "weather-icon";
      icon.append(uiIcon(weatherIconKey(weather)));
      panel.append(icon, el("h2", t("currentWeather")), el("strong", t(weatherLabelKey(weather))));
      const gameTime = formatGameTime(this.store.capabilities);
      if (gameTime) {
        const clock = el("div", undefined, "weather-game-time");
        clock.dataset.key = "weather-game-time";
        clock.append(el("span", t("gameTimeLabel"), "weather-game-time-label"));
        const time = el("time", gameTime, "weather-game-time-value");
        time.dateTime = gameTime;
        clock.append(time);
        panel.append(clock);
      }
      if (weather.dataQuality !== "ready") panel.append(el("p", t("weatherUnavailable"), "integration-note"));
      else {
        if (weather.wetnessKnown && Number.isFinite(Number(weather.wetness)))
          panel.append(el("p", t("wetness") + ": " + number(weather.wetness * 100, 0) + "%"));
        const measurements = weatherMeasurements(weather);
        if (measurements.length) {
          const dl = el("dl", undefined, "weather-measurements");
          for (const measurement of measurements) {
            dl.append(el("dt", t(measurement.key)), el("dd", number(measurement.value * 100, 0) + "%"));
          }
          panel.append(dl);
        }
      }
      const forecast = el("section", undefined, "weather-forecast");
      forecast.dataset.key = "weather-forecast";
      forecast.append(el("h3", t("weatherForecast")));
      if (!weather.forecastKnown || !Array.isArray(weather.forecast) || !weather.forecast.length) {
        forecast.append(el("p", t("weatherForecastUnavailable"), "integration-note"));
      } else {
        const list = el("ol", undefined, "weather-forecast-list");
        for (const [index, item] of weather.forecast.entries()) {
          const forecastWeather = { ...item, dataQuality: "ready" };
          const itemState = weatherState(forecastWeather);
          const row = el("li");
          row.dataset.key = "weather-forecast-" + index;
          if (item.timeKnown && Number.isInteger(item.hourStart) && Number.isInteger(item.hourEnd)) {
            const hours = value => String((value % 24 + 24) % 24).padStart(2, "0") + ":00";
            row.append(el("time", hours(item.hourStart), "weather-time"));
            row.title = hours(item.hourStart) + "–" + hours(item.hourEnd) + " · " + t("gameTimeLabel");
          }
          const forecastIcon = el("span", undefined, "weather-icon weather-" + itemState);
          forecastIcon.append(uiIcon(weatherIconKey(forecastWeather)));
          row.append(forecastIcon, el("span", t(weatherLabelKey(forecastWeather)), "weather-condition"));
          if (item?.timeKnown && !Number.isInteger(item.hourStart) && Number.isFinite(item.startsInSeconds) && item.startsInSeconds >= 0)
            row.append(el("small", item.startsInSeconds === 0 ? t("weatherForecastNow") : t("weatherForecastIn").replace("{time}", duration(item.startsInSeconds))));
          if (!Number.isInteger(item.hourStart) && item?.durationKnown && Number.isFinite(item.durationSeconds))
            row.append(el("small", t("weatherForecastDuration").replace("{time}", duration(item.durationSeconds))));
          list.append(row);
        }
        forecast.append(list);
      }
      panel.append(forecast);
      const fragment = document.createDocumentFragment();
      fragment.append(panel);
      // Keep the panel and forecast rows keyed so capability polling updates
      // text/icon attributes without detaching an open browser selection or
      // causing a visible full-panel blink.
      syncChildren(root, fragment);
      return;
    }
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
      icon.append(this.tab === "trains" ? locomotiveIcon(item) : uiIcon(this.tab));
      if (this.tab === "jobs") {
        icon.replaceChildren(jobIcon(item));
        icon.classList.add("job-icon");
        icon.style.color = jobColor(item);
      } else if (this.tab === "trains") {
        icon.classList.add("locomotive-row-icon");
        icon.style.color = /^#[a-f\d]{6}$/i.test(item.catalogColor || "")
          ? item.catalogColor
          : neutralJobColor;
      } else if (rowKind === "cars" || rowKind === "wagonGroups") {
        const visualJob = carJobVisual(this.store, item);
        if (visualJob) {
          icon.replaceChildren(jobIcon(visualJob));
          icon.classList.add("job-icon");
          icon.style.color = jobColorForStore(this.store, visualJob);
        } else {
          icon.style.color = neutralJobColor;
        }
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
    if (!this.detailNavigation) this.navigationStack = [];
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
  pickNested(kind, item) {
    if (!item) return;
    if (this.selected && (this.selected.kind !== kind || this.selected.id !== item.id))
      this.pushDetailsContext();
    this.detailNavigation = true;
    try { this.pick(kind, item); } finally { this.detailNavigation = false; }
  }
  pushDetailsContext() {
    const inspector = $("inspector");
    this.navigationStack.push({selection:{...this.selected},scroll:inspector.scrollTop,
      disclosures:[...inspector.querySelectorAll('details[data-key]')].map(node=>[node.dataset.key,node.open]),
      camera:{cx:this.renderer.cx,cz:this.renderer.cz,scale:this.renderer.scale},
      routeTab:this.routeDetailTabs?.get(this.selected.id)});
    if(this.navigationStack.length>32)this.navigationStack.shift();
  }
  backDetails() {
    while(this.navigationStack.length) {
      const previous=this.navigationStack.pop(), selection=previous.selection;
      const item=selection.kind==='warnings'?this.store.routes.find(r=>r.id===selection.route):this.renderer.resolve(selection);
      if(!item)continue;
      this.selected=selection;
      this.renderer.selected=selection.kind==='warnings'?{kind:selection.warning.kind,id:selection.warning.target}:selection;
      this.renderer.cameraFocus=null;this.renderer.follow=null;
      Object.assign(this.renderer,previous.camera);
      // Restoring the camera changes every projected pixel. Mark all render
      // layers and the hit-test index dirty through the renderer's public
      // invalidation path; setting ad-hoc flags left the static track canvas
      // at the child-detail transform until the next pan/resize.
      this.renderer.invalidate?.();
      this.renderer.staticDirty = this.renderer.overlayDirty = this.renderer.interactionDirty = true;
      if(previous.routeTab)this.routeDetailTabs.set(selection.id,previous.routeTab);
      this.renderInspector();
      const inspector=$("inspector"),open=new Map(previous.disclosures);
      for(const node of inspector.querySelectorAll('details[data-key]'))if(open.has(node.dataset.key))node.open=open.get(node.dataset.key);
      inspector.scrollTop=previous.scroll;this.syncTrackingControls();return true;
    }
    return false;
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
  recalculateRoute(route) {
    if (!route?.id || route.endedAt || !route.canRecalculate) return;
    return this.command("recalculateRoute", { target: route.id });
  }
  beginRoute(id, train = null) {
    this.waypointBefore=null;
    this.clearRoutePreview();
    this.startTrack = id;
    this.startTrain = train;
    this.endTrack = null;
    this.routeVia = [];
    this.routeJobId = null;
    this.routeTaskIndex = -1;
    this.routeTaskId = null;
    this.routeEditingId = null;
    this.requiredRouteVia = [];
    this.renderRoute();
    this.renderInspector();
  }
  beginRouteEdit(route) {
    if(!route?.id || route.endedAt || ['preview','replacing','unconfirmed','recalculating'].includes(route.recalculationState))return;
    this.beginRoute(route.from,route.trainCar || route.train);
    this.routeEditingId=route.id;this.routeVia=[...(route.via||[])];this.endTrack=route.to;
    this.routeJobId=route.jobId;this.routeTaskIndex=route.taskIndex;this.routeTaskId=route.taskId;
    const job=this.store.jobs.get(route.jobId);
    if(job && passengerJob(job))this.routeVia=this.routeVia.filter(id=>!job.legs.some(l=>l.passengerStop&&l.toTrack===id)||job.legs.some(l=>l.passengerStop&&l.toTrack===id&&l.progress!=='completed'));
    this.requiredRouteVia=job && passengerJob(job)?this.routeVia.filter(id=>job.legs.some(l=>l.passengerStop&&l.toTrack===id)):[];
    this.previewRoute();
  }
  routeRequestContext() {
    return {jobId:this.routeJobId,taskIndex:this.routeTaskIndex,taskId:this.routeTaskId,editingId:this.routeEditingId,avoidReservations:this.avoidForeignReservations};
  }
  setRouteDestination(id) {
    if(this.routeEditingId || this.routeJobId)return;
    if (!this.startTrack || !this.store.tracks.has(id) || this.routeVia.includes(id)) {
      this.toast(t("routeWaypointInvalid"), true);
      return;
    }
    if (this.endTrack === id && (this.previewPending || this.store.preview)) return;
    this.endTrack = id;
    this.previewRoute();
    this.renderInspector();
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
  toggleSwitchFromMap(item) {
    // Returning false tells the shared map input to keep the legacy selection
    // behaviour when the preference is disabled.  Once enabled, a valid
    // switch click is consumed even when the authoritative command is
    // rejected, so a safety error never opens a misleading details panel.
    if (!preferences.switchClick) return false;
    const junction = this.store.junctions.get(item?.id), state = this.store.switches.get(item?.id);
    if (!junction || !Array.isArray(junction.branches) || !junction.branches.length) return false;
    if (!this.store.capabilities.authority) {
      this.toast(t("FORBIDDEN"), true);
      return true;
    }
    const current = Number.isInteger(state?.branch) ? state.branch : Number.isInteger(item.branch) ? item.branch : -1;
    if (current < 0 || current >= junction.branches.length) {
      this.toast(t("STALE_REVISION"), true);
      return true;
    }
    const next = (current + 1) % junction.branches.length;
    this.command("setSwitch", {
      target: junction.id,
      branch: next,
      expectedRevision: state?.revision,
    });
    return true;
  }
  confirmAction(message, action, alternative=null) {
    return new Promise(resolve => {
      const dialog=el("dialog",undefined,"action-dialog");
      dialog.append(el("p",t(message)));
      const finish=value=>{dialog.close?.();dialog.remove();resolve(value);};
      const actions=el("div",undefined,"dialog-actions");
      const yes=el("button",t(action),"dialog-primary"),no=el("button",t("cancel"));
      yes.onclick=()=>finish(true);no.onclick=()=>finish(false);
      dialog.oncancel=e=>{e.preventDefault();finish(false);};
      actions.append(yes);
      if(alternative){const extra=el("button",t(alternative));extra.onclick=()=>finish("skip");actions.append(extra);}
      actions.append(no); dialog.append(actions); document.body.append(dialog);
      if(dialog.showModal)dialog.showModal();else dialog.setAttribute("open","");
    });
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
      document.body.classList.remove("mobile-sheet-open", "mobile-sheet-collapsed");
      $("inspector-frame")?.style.removeProperty("--mobile-sheet-offset");
      $("mobile-sheet-toggle")?.setAttribute("aria-expanded", "false");
      return;
    }
    $("mobile-sheet-toggle")?.setAttribute("aria-expanded", "true");
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
      document.body.classList.remove("mobile-sheet-open", "mobile-sheet-collapsed");
      return;
    }
    if (target.hidden) target.hidden = false;
    root.classList.add("detail-surface", "detail-kind-" + kind);
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
    if (this.navigationStack.length && kind !== 'warnings') header.append(detailsBack(this));
    const close = el("button", "×");
    close.setAttribute("aria-label", t("close"));
    close.onclick = () => {
      this.navigationStack = [];
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
        // syncChildren preserves a user's disclosure while this selection is
        // open. A new inspector selection starts collapsed by default.
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
          if (current) this.pickNested(relationKind, current);
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
          if ((block.extraTracks || []).includes(id)) badges.append(el("span", t("nativeProtection"), "status-badge reserved"));
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
      if (!this.startTrack) this.button(actions, "startRoute", () => this.beginRoute(id));
      else {
        this.button(actions, "endRoute", () => this.setRouteDestination(id), false,
          !this.planning && !this.routeEditingId && !this.routeJobId && !this.routeVia.includes(id));
        this.button(actions, "addWaypoint", () => this.addWaypoint(id), false,
          !this.planning && id !== this.startTrack && id !== this.endTrack && !this.routeVia.includes(id));
      }
    }
    if (kind === "cars" || kind === "trains" || kind === "wagonGroups") {
      if (kind === "trains") this.kv(root, "type", t(trainDescription(item)));
      this.kv(root, "speed", number(Math.abs(item.speed), 1) + " " + t("kmh"));
      this.kv(root, "length", Number.isFinite(item.length) ? number(item.length, 1) + " " + t("meters") : t("lengthUnavailable"));
      if(kind==="cars"&&item.locomotive) {
        const service=consistService(this.store,item);
        this.kv(root,"consistLength",service.length===null?t("lengthUnavailable"):number(service.length,1)+" "+t("meters"));
      } else if (kind === "trains" || kind === "wagonGroups")
        this.kv(root, "consistLength", Number.isFinite(item.length) ? number(item.length, 1) + " " + t("meters") : t("lengthUnavailable"));
      if (item.massKnown === true && Number.isFinite(Number(item.mass)))
        this.kv(root, "weight", number(item.mass, 1) + " " + t("tons"));
      else this.kv(root, "weight", t("massUnavailable"));
      if ((kind === "trains" || kind === "wagonGroups" || item.locomotive) && item.massKnown === true && Number.isFinite(Number(item.consistMass ?? item.mass)))
        this.kv(root, "consistWeight", number(item.consistMass ?? item.mass, 1) + " " + t("tons"));
      if (item.locomotive || kind === "trains") {
        const traction = el("section", undefined, "traction-capacity");
        traction.dataset.key = "traction-capacity";
        traction.append(el("h3", t("tractionCapacity")));
        const rating = item.tractionRating || locomotiveLoadRating(item.catalogModel || item.model || item.name);
        if (rating) {
          traction.append(el("p", `${t("loadRatingReference")} · ${rating.key}`, "integration-note"));
          traction.append(el("p", `${t("gradeDry")} 0%: ${number(rating.dry0)} ${t("tons")} · 2%: ${number(rating.dry2)} ${t("tons")}`));
          traction.append(el("p", `${t("gradeWet")} 2%: ${number(rating.wet2)} ${t("tons")}`));
        }
        if (item.tractionKnown === true && Number.isFinite(Number(item.availableTraction))) {
          traction.append(el("p", `${t("generatedTraction")}: ${number(item.availableTraction, 0)} N`, "integration-note"));
          traction.append(el("small", t("generatedTractionHint")));
        } else traction.append(el("p", t("tractionUnavailable"), "integration-note"));
        const assessment = tractionAssessment(item, this.store.capabilities.weather);
        if (assessment.limit != null) {
          traction.append(el("p", `${t("tractionAssessment")}: ${t(assessment.key)} · ${t("tractionAssessmentBasis")} ${number(assessment.limit)} ${t("tons")}`, "integration-note"));
        } else {
          traction.append(el("p", `${t("tractionAssessment")}: ${t("tractionUnknown")}`, "integration-note"));
        }
        root.append(traction);
      }
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
        link.onclick = () => this.pickNested("routes", protectedRoute);
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
      const jobs=[...this.store.jobs.values()].filter(job=>item.identityKey &&
        (job.assignedPlayerKey||job.ownerKey)===item.identityKey && (job.active||job.state==='Available'));
      const orders=el('section',undefined,'job-progress');orders.dataset.key='player-orders';
      orders.append(el('h3',t('playerOrders')+' · '+jobs.length),el('p',t('independentOrdersHint')));
      for(const job of jobs) {
        const row=el('div',undefined,'job-progress-item');row.dataset.key='player-order-'+job.id;
        const open=el('button',entityName(this.store,'jobs',job));open.onclick=()=>this.pickNested('jobs',this.store.jobs.get(job.id));
        row.append(open);
        for(const leg of job.legs||[])if(leg.progress==='active')row.append(el('small',t('task_active')+' · '+localizedValue(leg.operation||leg.type)));
        orders.append(row);
      }
      root.append(orders);
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
    if (target.dataset.selection !== selection) {target.replaceChildren();target.scrollTop=0;}
    target.dataset.selection = selection;
    syncChildren(target, root);
    tickClocks(this);
  }
  loco(root, car) {
    this.kv(root, "brakePipe", number(car.brakePipe, 1) + " " + t("pressureBar"));
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
    select.setAttribute("aria-label",t("uncouplingPosition"));
    for (let i = car.frontCount; i >= -car.rearCount; i--) {
      if (i === 0) continue;
      const o = el("option", t(i>0?"couplerFront":"couplerRear").replace("{index}",number(Math.abs(i))));
      o.value = i;
      select.append(o);
    }
    // Keep the coupling position and its action together.  A detached
    // selector was easy to miss and, on narrow details panels, looked like a
    // second unrelated control.  The stable data keys remain unchanged for
    // retained DOM and automation clients.
    const uncoupleControl = el("div", undefined, "coupler-control");
    uncoupleControl.dataset.key = "uncouple-control";
    uncoupleControl.append(select);
    this.button(
      uncoupleControl,
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
    actions.append(uncoupleControl);
    root.append(actions);
  }
  addWaypoint(id) {
    if (!id || !this.store.tracks.has(id) || !this.startTrack || this.planning) return;
    if (id === this.startTrack || id === this.endTrack || this.routeVia.includes(id)) {
      this.toast(t("routeWaypointInvalid"), true);
      this.renderRoute();
      return;
    }
    const at=this.routeVia.indexOf(this.waypointBefore);
    this.routeVia = [...this.routeVia];this.routeVia.splice(at<0?this.routeVia.length:at,0,id);
    this.store.preview = null;
    this.renderer.overlayDirty = true;
    this.renderRoute();
    this.previewRoute();
  }
  removeWaypoint(index) {
    if (index < 0 || index >= this.routeVia.length || this.requiredRouteVia?.includes(this.routeVia[index])) return;
    this.routeVia = this.routeVia.filter((_, i) => i !== index);
    this.store.preview = null;
    this.renderRoute();
    this.previewRoute();
  }
  moveWaypoint(index, delta) {
    const target = index + delta;
    if (index < 0 || target < 0 || target >= this.routeVia.length) return;
    if(this.requiredRouteVia?.includes(this.routeVia[index]))return;
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
      this.avoidForeignReservations,
      this.startTrain,
      this.routeJobId ? this.store.jobs.get(this.routeJobId)?.legs?.map(l=>[l.id,l.progress,l.toTrack]) : null,
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
        s.routeIncoming,
        s.routeBranches,
        s.routeIncomingDirection,
        s.routeBranchDirections,
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
      (this.routeJobId && (p.replaceJobs || p.jobs?.some(j=>j.id===this.routeJobId))) ||
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
  async previewRoute(retry = 0) {
    if(this.previewPending && this.previewKey === this.previewStateKey())return;
    const retainedPreview = this.store.preview?.from === this.startTrack &&
      this.store.preview?.to === this.endTrack &&
      JSON.stringify(this.store.preview?.via || []) === JSON.stringify(this.routeVia)
      ? this.store.preview : null;
    this.clearRoutePreview(true);
    if (retainedPreview && !this.store.disconnected && this.startTrack && this.endTrack) {
      this.store.preview = retainedPreview;
      this.renderer.overlayDirty = true;
    }
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
        this.routeRequestContext(),
      );
      if (request !== this.routeRequest) return;
      if (key !== this.previewStateKey()) {
        if (retry < 2) return this.previewRoute(retry + 1);
        throw new Error("ROUTE_CALCULATION_INVALIDATED");
      }
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
        // A failed/no-path response must not leave an older path actionable.
        // The retained preview is only a visual bridge while a newer request
        // is pending; once that request fails, remove it before showing the
        // authoritative error.
        this.store.preview = null;
        this.renderer.overlayDirty = true;
        this.previewKey = null;
        this.planningError = e.message || "COMMAND_FAILED";
        this.toast(t(e.message), true);
        this.renderRoute();
      }
    } finally {
      if (request === this.routeRequest) {
        this.previewPending = false;
        this.renderRoute();
      }
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
      this.avoidForeignReservations,
      this.routeRequestContext(),
    ]);
  }
  async planPreview() {
    if (
      this.planning ||
      this.previewPending ||
      this.plannedRoute ||
      !this.startTrack ||
      !this.endTrack ||
      !this.store.preview
    )
      return;
    const context = this.planningContext();
    const job=this.store.jobs.get(this.routeJobId);
    if(job?.state==="Available") {
      const decision=await this.confirmAction("acceptJobPrompt","acceptAndContinue","planWithoutAccepting");
      if(!decision)return;
      if(context!==this.planningContext())return;
      if(decision!=="skip") {
        const accepted=await this.command("acceptJob",{target:job.id});
        if(accepted.status!=="applied"||context!==this.planningContext())return;
        if(!this.store.jobs.get(job.id)?.active){this.toast(t("JOB_STATE_CHANGED"),true);return;}
      }
    }
    this.planning = true;
    this.planningError = null;
    this.planningPreview = this.store.preview;
    this.renderRoute();
    try {
      const result = await this.command(this.routeEditingId ? "editRoutePoints" : "planRoute", {
        target:this.routeEditingId,
        from: this.startTrack,
        to: this.endTrack,
        train: this.startTrain || null,
        via: [...this.routeVia],
        avoidReservations: this.avoidForeignReservations,
        jobId: this.routeJobId,
        taskIndex: this.routeTaskIndex,
        taskId: this.routeTaskId,
      });
      if (context !== this.planningContext()) return;
      if (result.status !== "applied" || !result.target) {
        this.planningError = result.code || "COMMAND_FAILED";
        this.renderRoute();
        return;
      }
      this.plannedRoute = { id: result.target, context };
      // The final receipt carries the server's confirmed route, closing the
      // receipt/routes-stream race without choosing by name or creating a plan.
      const known = this.store.routes.find((r) => r.id === result.target);
      if (
        result.route?.id === result.target &&
        (this.routeEditingId || !known || known.lifecycle === "preparing") &&
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
    if (!route || route.lifecycle === "preparing") {
      // An applied receipt must contain the confirmed plan or refer to a
      // confirmed streamed record. Do not wait forever for an unspecified
      // future frame when the server has already finished this operation.
      this.plannedRoute = null;
      this.planningError = "ROUTE_STATE_NOT_CONFIRMED";
      this.renderRoute();
      return;
    }
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
    this.routeJobId = null;
    this.routeTaskIndex = -1;
    this.routeTaskId = null;
    this.routeEditingId = null;
    this.requiredRouteVia = [];
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
      el("span", t("startRoute") + ":"),
      el(
        "strong",
        train
          ? entityName(this.store, "trains", train)
          : trackName(this.store, from || this.startTrack),
      ),
      el("span", "→"),
    );
    const orderJob=this.store.jobs.get(this.routeJobId);
    const stopNames=new Map(orderJob&&passengerJob(orderJob)?passengerStops(orderJob).map(stop=>[stop.track,passengerStopLabel(this.store,stop)]):[]);
    const pointLabel=id=>stopNames.get(id)||trackName(this.store,id);
    const destination = el("input"),
      options = el("datalist");
    options.id = "route-destinations";
    destination.setAttribute("list", options.id);
    destination.placeholder = t("selectEnd");
    destination.setAttribute("aria-label", t("destination"));
    destination.value = this.endTrack
      ? pointLabel(this.endTrack)
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
    destination.onchange = (e) => {
      const value = normalizeSearch(e.currentTarget.value);
      const matches = [...this.store.tracks.values()].filter(track =>
        [track.id, trackName(this.store, track)].some(name => normalizeSearch(name) === value));
      if (matches.length === 1) this.setRouteDestination(matches[0].id);
      else { this.planningError = "selectValidTrack"; this.renderRoute(); }
    };
    destination.onkeydown = (e) => { if (e.key === "Enter") { e.preventDefault(); e.currentTarget.onchange(e); } };
    destination.disabled = !!this.planning || !!this.routeEditingId || !!this.routeJobId;
    const waypoints = el("div", undefined, "route-waypoints");
    waypoints.dataset.key = "route-waypoints";
    if (this.routeVia.length) {
      waypoints.append(el("strong", t("routeWaypoints")));
      const list = el("ol");
      this.routeVia.forEach((id, index) => {
        const row = el("li", undefined, "route-waypoint");
        row.dataset.id = id;
        row.dataset.key = 'waypoint-'+id;
        row.append(el("span", number(index + 1) + ". " + pointLabel(id)));
        row.append(el('small',t(this.requiredRouteVia?.includes(id)?'orderStopRequired':'manualRouteWaypoints')));
        const actions = el("span", undefined, "route-waypoint-actions");
        const up = el("button", "↑");
        up.type = "button";
        up.title = t("moveWaypointUp");
        up.disabled = index === 0 || this.requiredRouteVia?.includes(id);
        up.onclick = () => this.moveWaypoint(index, -1);
        const down = el("button", "↓");
        down.type = "button";
        down.title = t("moveWaypointDown");
        down.disabled = index === this.routeVia.length - 1 || this.requiredRouteVia?.includes(id);
        down.onclick = () => this.moveWaypoint(index, 1);
        const remove = el("button", "×");
        remove.type = "button";
        remove.disabled = this.requiredRouteVia?.includes(id) === true;
        remove.title = t("removeWaypoint");
        remove.onclick = () => this.removeWaypoint(index);
        actions.append(up, down, remove);
        if(!this.requiredRouteVia?.includes(id))row.append(actions);
        list.append(row);
      });
      waypoints.append(list);
    }
    if (this.routeVia.length && !this.requiredRouteVia?.length) {
      const clearWaypoints = this.button(root, "clearWaypoints", () => {
        this.routeVia = [];
        this.store.preview = null;
        this.renderRoute();
        this.previewRoute();
      });
      clearWaypoints.dataset.key = "action-clear-waypoints";
      clearWaypoints.disabled = !!this.requiredRouteVia?.length;
    }
    root.append(waypoints);
    const reservationOption = el("label", undefined, "route-reservation-option"),
      avoidReservations = el("input");
    avoidReservations.type = "checkbox";
    avoidReservations.checked = this.avoidForeignReservations;
    avoidReservations.setAttribute("aria-label", t("avoidForeignReservations"));
    avoidReservations.onchange = (event) => {
      this.avoidForeignReservations = event.currentTarget.checked;
      this.store.preview = null;
      this.renderRoute();
      this.previewRoute();
    };
    reservationOption.append(avoidReservations, el("span", t("avoidForeignReservations")));
    reservationOption.dataset.key = "avoid-foreign-reservations";
    root.append(reservationOption);
    const addPoint=el('div',undefined,'route-waypoint-entry');addPoint.dataset.key='waypoint-entry';
    const pointInput=el('input');pointInput.dataset.key='waypoint-input';pointInput.dataset.preserveValue='true';
    pointInput.setAttribute('aria-label',t('addWaypoint'));pointInput.setAttribute('list',options.id);pointInput.placeholder=t('addWaypoint');
    pointInput.disabled=!!this.planning;
    pointInput.oninput=e=>suggest(e.currentTarget,document.getElementById('route-destinations'));
    const before=el('select');before.dataset.key='waypoint-before';before.setAttribute('aria-label',t('waypointBefore'));before.disabled=!!this.planning;
    for(const id of [...this.routeVia,this.endTrack].filter(Boolean)) {
      const option=el('option',pointLabel(id));option.value=id;option.selected=id===(this.waypointBefore||this.endTrack);before.append(option);
    }
    before.onchange=e=>{this.waypointBefore=e.currentTarget.value;};
    addPoint.append(pointInput,el('span',t('waypointBefore')),before);
    this.button(addPoint,'addWaypoint',()=>{
      const input=target.querySelector('[data-key="waypoint-input"]'),query=normalizeSearch(input.value);
      const matches=[...this.store.tracks.values()].filter(track=>[track.id,trackName(this.store,track)].some(name=>normalizeSearch(name)===query));
      if(matches.length!==1){this.toast(t('selectValidTrack'),true);return;}
      this.addWaypoint(matches[0].id);input.value='';
    },false,!this.planning);
    root.append(addPoint);
    root.append(el("span", t("destination") + ":"), destination, options);
    if (!this.endTrack) root.append(el("p", t("selectEnd"), "route-point-hint"));
    if (!this.endTrack) root.append(el("p", t("routePointActions"), "route-point-hint"));
    if (this.previewPending) root.append(el("span", t("calculatingRoute"), "route-calculation-status"));
    if (this.planningError) {
      root.append(el("p", localizedValue(this.planningError, "COMMAND_FAILED"), "warning route-planning-error"));
      if (!this.planning && !this.previewPending && this.endTrack)
        this.button(root, "retryRouteCalculation", () => this.previewRoute());
    }
    if (this.store.preview || this.planning || this.plannedRoute) {
      const preview =
        this.store.preview || this.planningPreview || this.lastPreview;
      if (preview) {
        root.append(el("span", number(preview.length) + " " + t("meters")));
        const estimate=routeTime(preview,this.store);
        if(estimate) {
          const time=el("span",t("routeEstimate").replace("{min}",number(estimate.min)).replace("{max}",number(estimate.max)),"route-time");
          time.title=t("routeEstimateHint");root.append(time);
        }
        const planButton = this.button(
          root,
          this.routeEditingId ? "previewRouteEdit" : "plan",
          () => this.planPreview(),
          true,
          !this.previewPending && !this.planning && !this.plannedRoute && !!this.store.preview,
        );
        if (this.routeJobId && !this.store.jobs.get(this.routeJobId)?.active)
          root.append(el("p", t("orderAcceptanceHint"), "route-point-hint"));
        if (this.previewPending || this.planning || this.plannedRoute)
          planButton.textContent = t(
            this.previewPending ? "calculatingRoute" : this.planning ? "planningRoute" : "waitingRoute",
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
      this.clearRoutePreview();
      this.startTrack = this.endTrack = this.startTrain = null;
      this.routeVia = [];
      this.routeJobId = null;
      this.routeTaskIndex = -1;
      this.routeTaskId = null;
      this.routeEditingId = null;
      this.requiredRouteVia = [];
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
    if (this.user?.role !== "admin") access.append(
      el("p", t("webAccessExplanation")),
      el(
        "p",
        t(
          this.user?.role === "dispatcher"
              ? "dispatcherAccessHelp"
              : "viewerAccessHelp",
        ),
      ),
    );
    if (this.user?.role !== "admin") return;
    accountSettings(this, access);
    this.connectionSettings(access);
    this.button(
      groups.dispatchSettings,
      "rescan",
      () => this.command("rescan"),
      true,
    );
  }
  async connectionSettings(access) {
    if (!this.network.connectionInfo) return;
    const info = el("details", undefined, "host-connection"), user = this.user;
    const summary = el("summary", t("hostConnection")), body = el("div");
    info.dataset.key = "host-connection-info";
    body.append(el("p", t("loadingConnection")));
    info.append(summary, body);
    access.append(info);
    try {
      const connection = await this.network.connectionInfo();
      if (!access.isConnected || this.user !== user) return;
      body.replaceChildren(el("p", t(connection.remoteAccess ? "remoteAccessEnabled" : "remoteAccessDisabled")));
      for (const address of connection.addresses || []) {
        const label = { Local: "addressLocal", LAN: "addressLan", "Radmin VPN": "addressVpn", Public: "addressPublic" }[address.label];
        const link = el("a", address.url);
        let url;
        try { url = new URL(address.url); } catch { continue; }
        if (!["http:", "https:"].includes(url.protocol)) continue;
        link.href = url.href;
        const row = el("div", undefined, "host-connection-address");
        row.append(el("small", label ? t(label) : t("hostConnection")), link);
        body.append(row);
      }
      if (connection.https && connection.certificate) {
        const certificate = connection.certificate;
        const trust = el("details", undefined, "host-certificate");
        trust.append(el("summary", t("certificateSetup")), el("p", t("certificateTrustHelp")), el("p", t("certificateFingerprint") + ": " + certificate.sha256));
        const expires = new Date(certificate.expires);
        if (Number.isFinite(expires.getTime())) trust.append(el("p", t("certificateExpires") + ": " + expires.toLocaleDateString(language())));
        const download = el("a", t("downloadCertificate"));
        download.href = "/api/certificate";
        download.download = "dispatcher-ca.cer";
        trust.append(download);
        body.append(trust);
      }
    } catch (error) {
      if (!access.isConnected || this.user !== user) return;
      body.replaceChildren(el("p", t(["HOST_TIMEOUT", "HOST_UNAVAILABLE", "AUTH_REQUIRED", "FORBIDDEN"].includes(error.message) ? error.message : "HOST_BAD_RESPONSE")));
      const retry = el("button", t("retryConnectionInfo"));
      retry.type = "button";
      retry.onclick = () => { info.remove(); this.connectionSettings(access); };
      body.append(retry);
    }
  }
}
