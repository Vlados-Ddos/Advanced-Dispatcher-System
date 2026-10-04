import { renderJobProgress, passengerJob, passengerStops, passengerStopLabel } from "./job-progress.js";
import { isWagon } from "./rolling-stock.js";
import { consistService } from "./consist-service.js";
import { tabLabel } from "./entity-search.js";
import { isJobAction, jobEventContent } from "./job-events.js";
import { zoomLimits } from "./camera.js";
import { signalParts } from "./signal-composite.js";
import {
  entityName,
  trackName,
  locationName,
  targetName,
  localizedValue,
  vehicleModel,
} from "./display-names.js";
import { aspectText } from "./signal-display.js";
import { settingsVolatile } from "./storage.js";
import { browserResetControls } from "./browser-settings-reset.js";
import {
  colorBadge,
  carJobAction,
  jobTypeText,
  jobTypeKey,
  jobColorForStore,
} from "./job-display.js";
import { $, el, syncChildren } from "./dom.js";
import { t, number, language } from "./localization.js";
import {
  preferences,
  limits,
  lodSettings,
  setPreference,
} from "./preferences.js";
import {
  jobElapsed,
  duration,
  jobTrains,
  trainBlocks,
  playersAboard,
  signalAhead,
  speedAt,
  dashboard,
  signSpeed,
} from "./operations.js";
import { routeMode, pickWarning } from "./route-details.js";

export function timer(job) {
  const node = el("span", "—", "order-clock");
  node.dataset.jobClock = job.id;
  return node;
}
export function tickClocks(ui) {
  for (const node of document.querySelectorAll("[data-job-start]")) {
    const job = ui.store.jobs.get(node.dataset.jobStart);
    if (job) {
      const text = jobStartText(job, ui.store);
      if (node.textContent !== text) node.textContent = text;
    }
  }
  for (const node of document.querySelectorAll("[data-job-clock]")) {
    const job = ui.store.jobs.get(node.dataset.jobClock);
    if (!job) continue;
    const remaining = job.bonusLimitSeconds - jobElapsed(job, ui.store);
    const text =
      job.dataQuality === "stale"
        ? "—"
        : !job.active
          ? job.elapsedKnown === false
            ? "—"
            : duration(job.elapsedSeconds)
          : remaining > 0
            ? duration(remaining)
            : t("bonusExpired");
    if (node.textContent !== text) node.textContent = text;
    node.classList.toggle("warning", job.active && remaining <= 60);
    node.title = t("bonusTime") + (ui.store.stale ? " · " + t("stale") : "");
  }
}
export function renderDashboard(ui) {
  const values = dashboard(ui.store),
    root = $("dashboard");
  if (!root.children.length)
    for (const key of Object.keys(values)) {
      const card = el("button", undefined, "stat");
      card.dataset.metric = key;
      card.append(el("strong", "0"), el("span", t(tabLabel(key))));
      card.onclick = () => {
        ui.setTab(
          {
            occupiedBlocks: "blocks",
            stopSignals: "signals",
            activeOrders: "jobs",
            warnings: "routes",
          }[key] || key,
        );
        $("active-only").checked = key !== "trains" && key !== "players";
        ui.dirty = true;
      };
      root.append(card);
    }
  for (const card of root.children) {
    const metric = card.dataset.metric,
      rawValue = Number(values[metric]) || 0,
      value = number(rawValue),
      label = t(tabLabel(card.dataset.metric));
    if (card.firstChild.textContent !== value)
      card.firstChild.textContent = value;
    // Keep the label neutral and colour only an actionable non-zero count.
    // This avoids repainting/rebuilding the dashboard when train motion is
    // streamed while still making live hazards visible at a glance.
    const tone = rawValue > 0
      ? ({
          occupiedBlocks: "metric-danger",
          stopSignals: "metric-danger",
          activeOrders: "metric-positive",
          warnings: "metric-warning",
        }[metric] || "")
      : "";
    card.firstChild.className = tone;
    if (card.lastChild.textContent !== label)
      card.lastChild.textContent = label;
  }
}
export function activeOrders(ui) {
  const root = $("active-orders");
  root.hidden = ui.tab !== "jobs";
  if (root.hidden) return;
  const content = el("div");
  const jobs = [...ui.store.jobs.values()].filter(
    (j) => j.state === "InProgress" || (j.active && !j.state),
  );
  const button = el(
    "button",
    t("activeJobsTotal") + " · " + number(jobs.length),
    "active-order",
  );
  button.dataset.key = "active-job-summary";
  button.title = t("showActiveJobs");
  button.onclick = () => {
    $("status-filter").value = "all";
    $("active-only").checked = true;
    ui.collect();
    ui.renderRows();
  };
  content.append(button);
  syncChildren(root, content);
}
export function renderFilters(ui) {
  if (ui.tab === "jobs")
    ui.jobTypeSignature = [
      ...new Set(
        [...ui.store.jobs.values()].map((j) =>
          [j.type, j.typeName, j.typeLanguage].join(":"),
        ),
      ),
    ]
      .sort()
      .join("|");
  const status = $("status-filter"),
    sort = $("sort-order"),
    previous = status.value;
  status.replaceChildren();
  sort.replaceChildren();
  const filters =
    ui.tab === "jobs"
      ? [
          "all",
          "Available",
          "InProgress",
          "Completed",
          "Abandoned",
          "Expired",
          ...[...new Set([...ui.store.jobs.values()].map(jobTypeKey))]
            .sort()
            .map((type) => "type:" + type),
        ]
      : ui.tab === "log"
        ? [
            "all",
            "warning",
            "error",
            "job",
            "signal",
            "block",
            "train",
            "route",
            "reservation",
            "player",
            "command",
          ]
        : ui.tab === "routes"
          ? [
              "activeRoutes",
              "routeWarnings",
              "reservedRoutes",
              "routeHistory",
              "all",
            ]
          : ["all"];
  if (
    ui.tab === "jobs" &&
    previous.startsWith("type:") &&
    !filters.includes(previous)
  )
    filters.push(previous);
  for (const key of filters) {
    const option = el(
      "option",
      key.startsWith("type:")
        ? t("jobType") +
            ": " +
            jobTypeText(
              [...ui.store.jobs.values()].find(
                (j) => jobTypeKey(j) === key.slice(5),
              ) || { type: key.slice(5) },
            )
        : t(key),
    );
    option.value = key;
    status.append(option);
  }
  status.value =
    filters.includes(previous) && !(ui.tab === "routes" && previous === "all")
      ? previous
      : filters[0];
  status.hidden = filters.length === 1;
  for (const key of [
    "name",
    "speed",
    "status",
    "recent",
    ...(ui.tab === "jobs" ? ["player"] : []),
  ]) {
    const option = el("option", t("sort_" + key));
    option.value = key;
    sort.append(option);
  }
  sort.value = ui.sort || "name";
  status.onchange = () => {
    $("object-list").scrollTop = 0;
    ui.dirty = true;
  };
  sort.onchange = () => {
    ui.sort = sort.value;
    $("object-list").scrollTop = 0;
    ui.collect();
    ui.renderRows();
    ui.dirty = true;
  };
  $("list-filters").hidden = ["settings", "locations"].includes(ui.tab);
}
export function displaySettings(ui, root) {
  const groups = {};
  for (const key of [
    "interfaceSettings",
    "signalSettings",
    "mapSettings",
    "dispatchSettings",
    "accessSettings",
  ]) {
    const section = el("section", undefined, "settings-group");
    section.dataset.group = key;
    section.append(el("h3", t(key)));
    root.append(section);
    groups[key] = section;
  }
  groups.interfaceSettings.append(el("p", t("automaticLanguage")));
  const lodGroup = el("details", undefined, "lod-settings");
  lodGroup.dataset.key = "lod-settings";
  lodGroup.append(
    el("summary", t("lodSettings")),
    el("p", t("lodSettingsHint")),
  );
  groups.mapSettings.append(lodGroup);
  const tooltipLabel = el("label", t("signalTooltip")),
    tooltip = el("input");
  tooltip.type = "checkbox";
  tooltip.checked = preferences.signalTooltip;
  tooltip.setAttribute("aria-label", t("signalTooltip"));
  tooltip.onchange = (e) =>
    setPreference("signalTooltip", e.currentTarget.checked);
  tooltipLabel.prepend(tooltip);
  groups.signalSettings.append(tooltipLabel);
  const switchClickLabel = el("label", t("switchClick")),
    switchClick = el("input");
  switchClick.type = "checkbox";
  switchClick.checked = preferences.switchClick;
  switchClick.setAttribute("aria-label", t("switchClick"));
  switchClick.onchange = (e) => setPreference("switchClick", e.currentTarget.checked);
  switchClickLabel.prepend(switchClick);
  groups.mapSettings.append(switchClickLabel);
  if (settingsVolatile())
    groups.interfaceSettings.append(el("p", t("settingsVolatile"), "warning"));
  syncHostSettings(ui, root);
  for (const [key, [min, max]] of Object.entries(limits)) {
    const label = el("label", t(key)),
      value = el(
        "output",
        `${preferences[key]}${key === "renderRate" ? "" : "%"}`,
      ),
      input = el("input");
    input.id = "display-" + key;
    input.setAttribute("aria-label", t(key));
    label.setAttribute("for", input.id);
    input.type = "range";
    input.min = min;
    input.max = max;
    input.step = key === "renderRate" ? 15 : key in lodSettings ? 25 : 5;
    input.value = preferences[key];
    input.oninput = () => {
      setPreference(key, Number(input.value));
      value.textContent = `${preferences[key]}${key === "renderRate" ? "" : "%"}`;
    };
    label.append(value, input);
    if (key in lodSettings) lodGroup.append(label);
    else if (["signalScale", "indicatorScale"].includes(key)) {
      groups.signalSettings.append(label);
    } else if (["interfaceScale", "textScale"].includes(key))
      groups.interfaceSettings.append(label);
    else groups.mapSettings.append(label);
  }
  const colorLabel = el("label", t("branchColor")),
    color = el("input");
  color.type = "color";
  color.id = "display-branchColor";
  color.value = preferences.branchColor;
  color.setAttribute("aria-label", t("branchColor"));
  color.oninput = (e) => setPreference("branchColor", e.currentTarget.value);
  colorLabel.append(color);
  groups.mapSettings.append(colorLabel);
  const map = el("label", t("mapScale")),
    zoom = el("input");
  zoom.type = "range";
  zoom.id = "display-mapScale";
  zoom.setAttribute("aria-label", t("mapScale"));
  zoom.min = Math.log(zoomLimits.min);
  zoom.max = Math.log(zoomLimits.max);
  zoom.step = 0.01;
  zoom.value = Math.log(ui.renderer.scale);
  zoom.oninput = () =>
    ui.renderer.zoom(Math.exp(Number(zoom.value)) / ui.renderer.scale);
  zoom.onchange = () => ui.renderer.saveCamera?.();
  map.append(zoom);
  groups.mapSettings.append(map);
  browserResetControls(groups.interfaceSettings);
  ui.button(groups.mapSettings, "fit", () => ui.renderer.fit());
  return groups;
}

// Host settings arrive with capability deltas.  Update their disabled
// controls in place so a client never edits a stale value or loses an active
// browser-local display control while the game is publishing state.
function createHostSettings(hostSettings) {
    const host = el("div", undefined, "host-settings");
    host.dataset.hostSettings = "true";
    host.append(el("h4", t("hostSettings")), el("p", t("hostSettingsHint")));
    for (const [key, labelKey] of [
      ["readOnly", "hostReadOnly"],
      ["showUndiscovered", "hostShowUndiscovered"],
      ["adminControls", "hostAdminControls"],
    ]) {
      const label = el("label", t(labelKey)),
        input = el("input");
      input.id = "host-" + key;
      input.type = "checkbox";
      input.checked = hostSettings[key] === true;
      input.disabled = true;
      input.dataset.authoritative = "true";
      input.setAttribute("aria-label", t(labelKey));
      label.prepend(input);
      host.append(label);
    }
    const budgetLabel = el("label", t("hostCaptureBudgetMs")),
      budget = el("input");
    budget.id = "host-captureBudgetMs";
    budget.type = "range";
    budget.min = "0.3";
    budget.max = "2";
    budget.step = "0.05";
    budget.value = Number.isFinite(Number(hostSettings.captureBudgetMs))
      ? String(hostSettings.captureBudgetMs)
      : "";
    budget.disabled = true;
    budget.dataset.authoritative = "true";
    budget.setAttribute("aria-label", t("hostCaptureBudgetMs"));
    budgetLabel.append(budget);
    host.append(budgetLabel);
    return host;
}

export function syncHostSettings(ui, root) {
  const hostSettings = ui.store.capabilities?.hostSettings;
  let host = root.querySelector("[data-host-settings]");
  if (!hostSettings || typeof hostSettings !== "object") { host?.remove(); return true; }
  if (!host) {
    const group = root.querySelector('[data-group="dispatchSettings"]');
    if (!group) return false;
    host = createHostSettings(hostSettings);
    group.append(host);
  }
  for (const key of ["readOnly", "showUndiscovered", "adminControls"]) {
    const input = root.querySelector("#host-" + key);
    if (input) input.checked = hostSettings[key] === true;
  }
  const budget = root.querySelector("#host-captureBudgetMs");
  if (budget && document.activeElement !== budget)
    budget.value = Number.isFinite(Number(hostSettings.captureBudgetMs))
      ? String(hostSettings.captureBudgetMs)
      : "";
  return true;
}
export function jobOwnerText(job) {
  if (job.owner) return job.owner;
  if (["Available", "Expired"].includes(job.state)) return t("ownerUnassigned");
  return t(
    {
      local: "ownerLocal",
      shared: "ownerShared",
      assigned: "ownerAssigned",
      unassigned: "ownerUnassigned",
    }[job.ownerStatus] || "ownerUnavailable",
  );
}
export function jobStartText(job, store) {
  if (["Available", "Expired"].includes(job.state)) return t("notStarted");
  if (
    job.startedGameDate &&
    /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}$/.test(job.startedGameDate)
  ) {
    // The game calendar has no browser time zone. Format its fields as-is.
    const date = new Date(job.startedGameDate + "Z");
    if (Number.isFinite(date.getTime()))
      return (
        new Intl.DateTimeFormat(language(), {
          day: "2-digit",
          month: "2-digit",
          hour: "2-digit",
          minute: "2-digit",
          hourCycle: "h23",
          timeZone: "UTC",
        }).format(date) +
        " · " +
        t("gameTimeLabel")
      );
  }
  if (job.active && job.elapsedKnown !== false)
    return t("startedAgo").replace("{time}", duration(jobElapsed(job, store)));
  return t("startNotRecorded");
}
export function jobDetails(ui, root, job) {
  const kv = (key, value) => ui.kv(root, key, value),
    trains = jobTrains(job, ui.store);
  kv("status", t(job.state || (job.active ? "InProgress" : "Available")));
  if (job.dataQuality === "stale")
    root.append(el("p", t("jobDataStale"), "warning"));
  kv("owner", jobOwnerText(job));
  const jobActions=el("div",undefined,"actions");
  jobActions.dataset.key="job-actions";
  if(job.state==="Available")ui.button(jobActions,"acceptJob",()=>ui.command("acceptJob",{target:job.id}),true,job.dataQuality!=="stale");
  if(job.active)ui.button(jobActions,"cancelJob",async()=>{
    if(await ui.confirmAction("cancelJobPrompt","cancelJob"))await ui.command("cancelJob",{target:job.id});
  },true,job.dataQuality!=="stale");
  if(job.assignedPlayerKey)kv("assignedPlayer",job.assignedPlayerName || t("playerName"));
  if(job.state==="Available") {
    const select=el("select");select.dataset.key="assign-job-player";select.dataset.preserveValue="true";select.setAttribute("aria-label",t("assignPlayer"));
    for(const player of ui.store.players.values())if(player.identityKey) {
      const option=el("option",entityName(ui.store,"players",player));option.value=player.id;select.append(option);
    }
    if(job.assignedPlayerKey)ui.button(jobActions,"unassignJob",()=>ui.command("unassignJob",{target:job.id}),true);
    jobActions.append(select);
    ui.button(jobActions,"assignPlayer",()=>ui.command("assignJob",{target:job.id,action:document.querySelector('[data-key="assign-job-player"]')?.value}),true,select.options.length>0);
  }
  root.append(jobActions);
  const start = el("div", undefined, "kv");
  const startValue = el("span", jobStartText(job, ui.store));
  startValue.dataset.jobStart = job.id;
  start.append(el("span", t("startTime")), startValue);
  root.append(start);
  renderJobProgress(ui, root, job);
  const wagons = el("section", undefined, "job-wagons");
  wagons.dataset.key = "job-wagons";
  wagons.append(el("h3", t("jobWagons")));
  const ids = [...new Set(job.cars || [])].filter(
    (id) => !ui.store.cars.has(id) || isWagon(ui.store.cars.get(id)),
  );
  for (const id of ids) {
    const car = ui.store.cars.get(id);
    const button = el(
      "button",
      car ? car.name || t("carName") : t("jobWagonUnavailable"),
    );
    button.dataset.key = "job-car-" + id;
    button.disabled = !car || job.dataQuality === "stale";
    button.title = car ? t("focusJobWagon") : t("jobWagonUnavailable");
    button.onclick = () => {
      const current = ui.store.jobs.get(job.id),
        target = ui.store.cars.get(id);
      if (
        current?.cars?.includes(id) &&
        current.dataQuality !== "stale" &&
        target
      )
        ui.pickNested("cars", target);
      else ui.toast(t("jobWagonUnavailable"));
    };
    wagons.append(button);
  }
  if (ids.length) root.append(wagons);
  root.append(colorBadge(job, jobColorForStore(ui.store, job)));
  if (
    job.integrationStatus &&
    !["ready", "loading"].includes(job.integrationStatus)
  )
    root.append(el("p", t("jobIntegrationUnavailable"), "warning"));
  kv("from", locationName(ui.store, job.origin));
  kv("destination", locationName(ui.store, job.destination));
  kv(
    "cargo",
    job.cargo
      ?.filter((x) => x !== "None")
      .map((v) => localizedValue(v, "cargoUnavailable"))
      .join(", ") ||
      cargoText((job.cars || []).map((id) => ui.store.cars.get(id))),
  );
  const clock = el("div", undefined, "kv");
  const supplemental=el("details",undefined,"order-additional-details");
  supplemental.dataset.key="order-additional-details";
  supplemental.append(el("summary",t("orderAdditionalDetails")));
  root.append(supplemental);root=supplemental;
  clock.append(el("span", t(job.active ? "bonusTime" : "elapsed")), timer(job));
  root.append(clock);
  kv("bonus", number(job.bonus));
  root.append(el("small", t("bonusExplanation")));
  kv(
    "length",
    job.cars?.length ? number(job.length) + " " + t("meters") : t("unknown"),
  );
  kv(
    "mass",
    job.cars?.length && job.massKnown === true ? number(job.mass, 1) + " " + t("tons") : t("massUnavailable"),
  );
  kv(
    "payment",
    Number.isFinite(job.payment)
      ? new Intl.NumberFormat(language(), {
          style: "currency",
          currency: "USD",
          maximumFractionDigits: 0,
        }).format(job.payment)
      : t("unknown"),
  );
  kv(
    "licenses",
    job.licenses
      ?.map((v) => localizedValue(v, "additionalLicense"))
      .join(", ") || "—",
  );
  kv(
    "currentBlock",
    trains
      .flatMap((id) =>
        trainBlocks(id, ui.store).map((b) => entityName(ui.store, "blocks", b)),
      )
      .join(", ") || t("unknown"),
  );
  for (const id of trains) {
    const train = ui.store.train(id);
    if (!train) continue;
    const button = el("button", entityName(ui.store, "trains", train));
    button.onclick = () => ui.pickNested("trains", train);
    root.append(button);
    kv("currentLocation", trackName(ui.store, train.track1));
  }
  root.append(el("h3", t("eventHistory")));
  for (const event of ui.store.events
    .filter((e) => e.target === job.id)
    .slice(-20)
    .reverse())
    root.append(
      el(
        "p",
        new Date(event.time).toLocaleTimeString(language()) +
          " · " +
          localizedValue(event.code, "eventRecorded") +
          " · " +
          targetName(ui.store, event.target),
        "event-history",
      ),
    );
}
export function routeDetails(ui, root, route, scope = "inspector") {
  const prefix = scope === "preview" ? "preview-" : "";
  const tabKey =
    scope === "preview" ? "preview:" + route.from + ":" + route.to : route.id;
  const refresh = () => {
    ui.renderInspector();
    ui.renderRoute();
  };
  const tabs = ui.routeDetailTabs || (ui.routeDetailTabs = new Map());
  if (tabs.size > 200)
    for (const id of tabs.keys())
      if (!ui.store.routes.some((r) => r.id === id)) tabs.delete(id);
  const selected = tabs.get(tabKey) || "details";
  const bar = el("div", undefined, "detail-tabs");
  bar.dataset.key = "route-detail-tabs";
  bar.setAttribute("role", "tablist");
  for (const [key, label, count] of [
    ["details", "routeOverviewTab"],
    ["sequence", "routeSequenceTab"],
    ["warnings", "activeWarningsTab", route.conflicts?.length || 0],
    ["resolved", "resolvedWarningsTab", route.history?.length || 0],
  ]) {
    const b = el(
      "button",
      t(label) + (count === undefined ? "" : ` (${count})`),
    );
    b.dataset.key = "route-tab-" + key;
    b.id = prefix + "route-tab-" + key;
    b.setAttribute("role", "tab");
    b.setAttribute("aria-selected", String(key === selected));
    b.setAttribute("aria-controls", prefix + "route-detail-panel");
    b.tabIndex = key === selected ? 0 : -1;
    b.onclick = () => {
      tabs.set(tabKey, key);
      refresh();
    };
    b.onkeydown = (e) => {
      if (!["ArrowLeft", "ArrowRight", "Home", "End"].includes(e.key)) return;
      e.preventDefault();
      const keys = ["details", "sequence", "warnings", "resolved"];
      const i =
        e.key === "Home"
          ? 0
          : e.key === "End"
            ? keys.length - 1
            : (keys.indexOf(key) +
                (e.key === "ArrowRight" ? 1 : keys.length - 1)) %
              keys.length;
      tabs.set(tabKey, keys[i]);
      refresh();
      document.getElementById(prefix + "route-tab-" + keys[i])?.focus();
    };
    bar.append(b);
  }
  root.append(bar);
  const panel = el("section");
  panel.id = prefix + "route-detail-panel";
  panel.dataset.key = "route-panel-" + selected;
  panel.setAttribute("role", "tabpanel");
  panel.setAttribute("aria-labelledby", prefix + "route-tab-" + selected);
  root.append(panel);
  root = panel;
  if (selected === "warnings" || selected === "resolved") {
    const warnings =
      selected === "warnings"
        ? route.conflicts || []
        : [...(route.history || [])].reverse();
    if (!warnings.length)
      root.append(
        el(
          "p",
          t(selected === "warnings" ? "noRouteWarnings" : "noResolvedWarnings"),
          "empty-list",
        ),
      );
    for (const c of warnings) {
      const row = el("div", undefined, "warning-entry");
      row.dataset.key = "warning-" + c.id + ":" + c.firstSeen;
      const button = el(
        "button",
        conflictText(ui, c),
        selected === "warnings" ? "conflict" : "",
      );
      button.onclick = () => pickWarning(ui, route, c);
      row.append(button);
      row.append(
        el(
          "small",
          t(selected === "warnings" ? "warningActive" : "warningResolved") +
            (c.resolvedAt || c.firstSeen
              ? " · " +
                new Date(c.resolvedAt || c.firstSeen).toLocaleString(language())
              : ""),
        ),
      );
      root.append(row);
    }
    return;
  }
  if (selected === "details") {
    const status = el("section", undefined, "detail-section detail-status");
    status.append(el("h3", t("status")));
    routeMode(ui, status, route);
    root.append(status);
    const routeInfo = el("section", undefined, "detail-section detail-main");
    routeInfo.append(el("h3", t("routeDetails")));
    const linkedOrder=ui.store.jobs.get(route.jobId);
    const stops=linkedOrder&&passengerJob(linkedOrder)?passengerStops(linkedOrder):[];
    const pointName=id=>{
      const stop=stops.find(s=>s.track===id);
      return stop?passengerStopLabel(ui.store,stop):trackName(ui.store,id);
    };
    ui.kv(routeInfo, "from", pointName(route.from));
    ui.kv(routeInfo, "destination", pointName(route.to));
    const manual=!route.jobId || !stops.length ? (route.via||[]) : (route.via||[]).filter(id=>route.manualVia?.includes(id)||!stops.some(stop=>stop.track===id));
    const mandatory=(route.via||[]).filter(id=>!manual.includes(id));
    if(mandatory.length)ui.kv(routeInfo,'orderRequiredStops',mandatory.map(pointName).join(' → '));
    if(manual.length)ui.kv(routeInfo,'manualRouteWaypoints',manual.map(id=>trackName(ui.store,id)).join(' → '));
    if (route.id && !route.endedAt && !["preview", "replacing", "unconfirmed", "recalculating"].includes(route.recalculationState))
      ui.button(routeInfo, route.jobId ? "addRouteWaypoint" : "editRoutePoints", () => ui.beginRouteEdit(route));
    if (route.jobId) {
      const job = ui.store.jobs.get(route.jobId);
      ui.kv(routeInfo, "job", job ? entityName(ui.store, "jobs", job) : route.jobId);
      if (route.taskIndex >= 0) ui.kv(routeInfo, "task", number(route.taskIndex + 1));
      if (job) {
        const orderProgress = el("section", undefined, "detail-section order-route-progress");
        orderProgress.dataset.key = "order-route-progress";
        renderJobProgress(ui, orderProgress, job, false);
        root.append(orderProgress);
      }
    }
    ui.kv(
      routeInfo,
      "remainingDistance",
      number(route.remaining ?? route.length) + " " + t("meters"),
    );
    // Native signal blocks are released as the consist tail clears them.
    // Keep the original ordered route for history and expose the live
    // completed/current/pending sections separately so route details mirrors
    // the reservation footprint instead of pretending the whole path is
    // still held.
    const orderedTracks = Array.isArray(route.tracks) ? route.tracks : [];
    const releasedTracks = new Set(route.releasedTracks || []);
    const completedTracks = orderedTracks.filter((id) => releasedTracks.has(id));
    const pendingTracks = orderedTracks.filter((id) => !releasedTracks.has(id));
    const disclosureState = ui.routeSegmentDisclosure ||
      (ui.routeSegmentDisclosure = new Map());
    const appendSegmentDisclosure = (key, label, tracks) => {
      if (!tracks.length) return;
      const details = el("details", undefined, "route-segment-disclosure");
      details.dataset.key = "route-segments-" + key;
      const stateKey = tabKey + ":" + key;
      const domId =
        "route-segments-list-" +
        String(stateKey).replace(/[^a-zA-Z0-9_-]/g, "-");
      const summary = el(
        "summary",
        t(label) + " · " + number(tracks.length),
      );
      summary.setAttribute("aria-controls", domId);
      const list = el("ol", undefined, "route-segment-list");
      list.id = domId;
      for (const id of tracks) list.append(el("li", trackName(ui.store, id)));
      details.append(summary, list);
      details.open = disclosureState.get(stateKey) === true;
      details.ontoggle = () => disclosureState.set(stateKey, details.open);
      routeInfo.append(details);
    };
    appendSegmentDisclosure("completed", "routeCompletedSegments", completedTracks);
    if (pendingTracks.length)
      ui.kv(routeInfo, "routeCurrentSegment", trackName(ui.store, pendingTracks[0]));
    appendSegmentDisclosure("pending", "routePendingSegments", pendingTracks.slice(1));
    root.append(routeInfo);
    return;
  }
  const list = el("ol", undefined, "itinerary");
  list.dataset.key = "route-itinerary";
  const points = route.itinerary || [],
    pages = (ui.routeSequencePages ||= new Map()),
    pageSize = 100;
  while (pages.size > 200) pages.delete(pages.keys().next().value);
  const page = Math.max(
    0,
    Math.min(pages.get(tabKey) || 0, Math.ceil(points.length / pageSize) - 1),
  );
  pages.set(tabKey, page);
  if (points.length > pageSize) {
    const navigation = el("div", undefined, "sequence-navigation");
    navigation.dataset.key = "sequence-navigation";
    for (const [label, delta] of [
      ["sequencePrevious", -1],
      ["sequenceNext", 1],
    ]) {
      const button = el("button", t(label));
      button.dataset.key = label;
      button.disabled =
        page + delta < 0 || (page + delta) * pageSize >= points.length;
      button.onclick = () => {
        pages.set(tabKey, page + delta);
        refresh();
      };
      navigation.append(button);
    }
    navigation.append(
      el(
        "span",
        `${t("sequencePage")} ${number(page + 1)} / ${number(Math.ceil(points.length / pageSize))}`,
      ),
    );
    root.append(navigation);
  }
  list.setAttribute("start", String(page * pageSize + 1));
  if (route.staged && route.stages?.length) {
    const stages = el("section", undefined, "detail-section staged-sequence");
    stages.dataset.key = "staged-sequence";
    stages.append(el("h3", t("stagedRoute")));
    const stageList = el("ol");
    for (const [index, stage] of route.stages.entries()) {
      const row = el("li", undefined, "stage-row stage-" + stage.status);
      row.dataset.key = "route-stage-" + index;
      row.append(el("strong", `${t("stage")} ${number(index + 1)}`), el("span", `${trackName(ui.store, stage.from)} → ${trackName(ui.store, stage.to)}`), el("small", t("stage_" + stage.status)));
      stageList.append(row);
    }
    stages.append(stageList);
    root.append(stages);
  }
  for (const [localIndex, point] of points
    .slice(page * pageSize, (page + 1) * pageSize)
    .entries()) {
    const index = page * pageSize + localIndex;
    const item = ui.renderer.resolve({ kind: point.kind, id: point.id }),
      row = el("li"),
      button = el("button");
    row.dataset.key = "point-" + index + ":" + point.kind + ":" + point.id;
    button.textContent = `${point.current ? t("currentBlock") : t(point.kind)} · ${entityName(ui.store, point.kind, item || point.id)} · ${number(point.distance)} ${t("meters")}`;
    if(point.boundary)button.textContent += " · " + t("signalBeyondDestination");
    button.onclick = () => {
      const current = ui.renderer.resolve({ kind: point.kind, id: point.id });
      if (current) ui.pickNested(point.kind, current);
    };
    button.disabled = !item;
    row.append(button);
    if (point.kind === "signals" && item) {
      row.append(
        el(
          "small",
          aspectText(item) + " · " + t(item.reserved ? "reserved" : "none"),
        ),
      );
      if (ui.store.capabilities.signalCommands && !route.endedAt && !point.boundary)
        ui.button(
          row,
          item.reserved ? "cancelSignal" : "reserveSignal",
          () =>
            ui.command(
              item.reserved ? "cancelSignalReservation" : "reserveSignal",
              { target: item.id, value: 120, expectedRevision: item.revision },
            ),
          true,
        );
    }
    list.append(row);
  }
  root.append(list);
}
export function conflictText(ui, c) {
  const item =
    c.kind === "trains"
      ? ui.store.train(c.target)
      : c.kind === "routes"
        ? ui.store.routes.find((r) => r.id === c.target)
        : ui.renderer.resolve({ kind: c.kind, id: c.target });
  return (
    localizedValue(c.code, "eventRecorded") +
    " · " +
    entityName(ui.store, c.kind, item || c.target) +
    (c.track && c.track !== c.target
      ? " · " + trackName(ui.store, c.track)
      : "") +
    (c.train ? " · " + entityName(ui.store, "trains", c.train) : "")
  );
}
export function extraInspector(ui, root, actions, kind, item) {
  const kv = (k, v) => ui.kv(root, k, v),
    s = ui.store;
  if (kind === "trains" || kind === "cars" || kind === "wagonGroups") {
    if (item.availability && item.availability !== "available")
      kv("availability", t(item.availability));
    if (item.locomotive || kind === "trains") {
      const car = kind === "trains" ? s.cars.get(item.head) : item;
      const service = consistService(s, car);
      const section = el("section", undefined, "consist-jobs");
      section.dataset.key = "consist-jobs";
      section.append(el("h3", t("consistJobs")));
      for (const job of service.jobs) {
        const button = el("button", job.id + " · " + jobTypeText(job));
        button.dataset.key = "consist-job:" + job.id;
        button.onclick = () => {
          const current = consistService(s, s.cars.get(car?.id)).jobs.find(
            (j) => j.id === job.id,
          );
          if (current) ui.pickNested("jobs", current);
        };
        section.append(button);
      }
      if (!service.complete || !service.jobs.length)
        section.append(
          el(
            "p",
            t(service.complete ? "consistNoActiveJob" : "consistJobUnknown"),
          ),
        );
      root.append(section);
    }
    const carIds =
      kind === "trains" || kind === "wagonGroups"
        ? item.carIds
        : item.locomotive
          ? [...(s.consists.get(item.consist) || [item.id])]
          : [item.id];
    kv(
      item.locomotive && kind === "cars" ? "consistCargo" : "cargo",
      cargoText(
        (carIds || []).map((id) => s.cars.get(id)),
        item.locomotive ? item.consistCargo : null,
      ),
    );
    if (item.model) kv("vehicleModel", vehicleModel(item));
    const trainId =
      kind === "trains" || kind === "wagonGroups" ? item.id : item.consist;
    kv(
      "aboard",
      playersAboard(trainId, s)
        .map((p) => p.name)
        .join(", ") || "—",
    );
    kv(
      "currentBlock",
      trainBlocks(trainId, s)
        .map((b) => entityName(ui.store, "blocks", b))
        .join(", ") || t("unknown"),
    );
    const next = signalAhead(item, s);
    kv(
      "nextSignal",
      next
        ? `${entityName(s, "signals", next.signal)} · ${aspectText(next.signal)} · ${number(next.distance)} ${t("meters")}`
        : t("unknown"),
    );
    const limit = speedAt(item, s);
    kv("speedLimits", limit === null ? t("unknown") : limit + " " + t("kmh"));
    ui.button(actions, "routeFromTrain", () => {
      ui.beginRoute(item.track1,trainId);
    });
    carJobAction(ui, actions, carIds);
  }
  if (kind === "signals") {
    if (signalParts(item).some((p) => p.kind === "routeIndicator"))
      root.append(el("p", t("routeIndicatorAutomatic"), "integration-note"));
    if (signalParts(item).some((p) => p.kind === "departureIndicator"))
      root.append(
        el("p", t("departureIndicatorAutomatic"), "integration-note"),
      );
    for (const part of signalParts(item).filter((p) => p.visible !== false))
      kv(
        ["departureIndicator", "routeIndicator", "auxiliaryIndicator"].includes(
          part.kind,
        )
          ? part.kind === "departureIndicator"
            ? "departureState"
            : part.kind
          : "auxiliaryDisplay",
        part.kind === "departureIndicator"
          ? t(
              !part.stateKnown
                ? "valueUnavailable"
                : part.active
                  ? "departureIndicator"
                  : "departureInactive",
            )
          : part.text || t("indicatorPresent"),
      );
    kv("blocks", entityName(s, "blocks", item.block));
    kv(
      "direction",
      t(
        item.direction > 0
          ? "alongTrack"
          : item.direction < 0
            ? "againstTrack"
            : "unknown",
      ),
    );
    kv(
      "passingSpeed",
      item.passingSpeed > 0
        ? number(item.passingSpeed) + " " + t("kmh")
        : t("noSignalSpeed"),
    );
  }
  if (kind === "blocks") {
    kv(
      "signals",
      [...s.signals.values()]
        .filter((x) => x.block === item.id)
        .map((x) => entityName(s, "signals", x))
        .join(", "),
    );
    kv(
      "direction",
      [...new Set(item.directions || [])]
        .map((d) =>
          t(d > 0 ? "alongTrack" : d < 0 ? "againstTrack" : "unknown"),
        )
        .join(", "),
    );
    if ((item.extraTracks || []).length) kv("nativeProtection", t("nativeProtection"));
  }
  if (kind === "switches") {
    kv(
      "connectedTracks",
      [item.incoming, ...(item.branches || [])]
        .map((id) => trackName(s, id))
        .join(", "),
    );
    kv("lock", t("unrestricted"));
    kv(
      "reservation",
      [...s.blocks.values()]
        .filter(
          (b) =>
            b.reserved &&
            b.tracks.some((id) =>
              [item.incoming, ...item.branches].includes(id),
            ),
        )
        .map((b) => entityName(ui.store, "blocks", b))
        .join(", ") || t("none"),
    );
    kv("controller", t("sharedControls"));
  }
  if (kind === "signs" && item.signalObject) {
    kv("source", "DV Signals");
    kv("type", localizedValue(item.signKind, "fixedBoard"));
    if (item.signKind === "turntableIndicator")
      kv(
        "aspect",
        t(
          item.turntableStateKnown
            ? item.turntableConnected
              ? "aspectTableConnected"
              : "aspectTableDisconnected"
            : "valueUnavailable",
        ),
      );
    else if (item.aspect && !item.off) kv("aspect", aspectText(item));
    if (item.text) kv("signText", item.text);
    if (item.track) kv("track", trackName(s, item.track));
  }
  if (kind === "signs" && !item.signalObject) {
    kv("track", trackName(s, item.track));
    kv(
      "speedLimits",
      item.advance ? t("advanceWarning") : (signSpeed(item, s) ?? t("unknown")),
    );
    kv("direction", t(item.direction > 0 ? "alongTrack" : "againstTrack"));
    kv("source", t(item.source));
    kv(
      "signs",
      item.types?.map((v) => localizedValue(v, "railwaySign")).join(", "),
    );
  }
  if (kind === "log") {
    if (isJobAction(item)) root.append(jobEventContent(ui, item));
  }
}

export function cargoText(cars, summary = null) {
  if (summary) {
    const names = [...new Set(summary.types || [])].map((v) =>
      localizedValue(v, "cargoUnavailable"),
    );
    return names.length
      ? names.join(", ") + (summary.complete ? "" : " · " + t("cargoPartial"))
      : t(summary.complete ? "cargoEmpty" : "cargoUnavailable");
  }
  if (!cars.length) return t("cargoUnavailable");
  const loaded = [
    ...new Set(
      cars
        .filter(
          (c) =>
            c?.cargoKnown && c.cargo && c.cargo !== "None" && c.cargoAmount > 0,
        )
        .map((c) => c.cargo),
    ),
  ];
  const expected = Math.max(
    0,
    ...cars
      .filter(Boolean)
      .map((c) => (c.frontCount || 0) + (c.rearCount || 0) + 1),
  );
  const missing =
    cars.length < expected ||
    cars.some(
      (c) =>
        !c ||
        (!c.locomotive &&
          (!c.cargoKnown ||
            !Number.isFinite(c.cargoAmount) ||
            c.cargoAmount < 0 ||
            (c.cargoAmount > 0 && (!c.cargo || c.cargo === "None")))),
    );
  if (loaded.length)
    return (
      loaded.map((v) => localizedValue(v, "cargoUnavailable")).join(", ") +
      (missing ? " · " + t("cargoPartial") : "")
    );
  return t(missing ? "cargoUnavailable" : "cargoEmpty");
}
