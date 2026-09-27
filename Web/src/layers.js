import { readSetting, saveSetting } from "./storage.js";
import { el } from "./dom.js";
import { t } from "./localization.js";

export const layerDefaults = Object.freeze({
  tracks: true,
  blocks: true,
  signals: true,
  shuntingSignals: true,
  switches: true,
  switchBranches: true,
  turntables: true,
  trains: true,
  players: true,
  routes: true,
  labels: true,
  blockLabels: false,
  stationMarkers: true,
  industryMarkers: true,
  passengerStops: true,
  reservations: true,
  speedLimits: false,
  speedRestrictions: false,
  signs: false,
  additionalSigns: false,
  technical: false,
});
export const layerGroups = [
  { id: "rail", title: "layerRail", children: [["tracks", "layerAllTracks"]] },
  {
    id: "junctions",
    title: "layerJunctions",
    children: [
      ["switches", "switchIcons"],
      ["switchBranches", "switchBranches"],
    ],
  },
  {
    id: "signals",
    title: "signals",
    children: [
      ["signals", "layerMainSignals"],
      ["shuntingSignals", "shuntingSignals"],
    ],
  },
  {
    id: "infrastructure",
    title: "layerInfrastructure",
    children: [
      ["stationMarkers", "layerStations"],
      ["industryMarkers", "layerIndustries"],
      ["turntables", "turntables"],
      ["passengerStops", "passengerStops"],
    ],
  },
  {
    id: "traffic",
    title: "layerTraffic",
    children: [
      ["trains", "layerRollingStock"],
      ["players", "players"],
    ],
  },
  {
    id: "dispatch",
    title: "layerDispatch",
    children: [
      ["routes", "routes"],
      ["blocks", "layerOccupation"],
      ["reservations", "reservations"],
    ],
  },
  {
    id: "speed",
    title: "layerSpeed",
    children: [
      ["signs", "signs"],
      ["speedLimits", "speedLimits"],
      ["speedRestrictions", "speedRestrictions"],
    ],
  },
  {
    id: "additionalSigns",
    title: "additionalRailwaySigns",
    children: [["additionalSigns", "additionalRailwaySigns"]],
  },
  {
    id: "labels",
    title: "layerLabels",
    children: [
      ["labels", "layerObjectNames"],
      ["blockLabels", "blockLabels"],
      ["technical", "technical"],
    ],
  },
];
export function normalizeLayers(saved) {
  saved = saved && typeof saved === "object" ? saved : {};
  const result = { ...layerDefaults };
  for (const key of Object.keys(result))
    if (typeof saved[key] === "boolean") result[key] = saved[key];
  // Old 'stations' rendered ALL stations, even when 'industries' was false.
  // Separate the actual disjoint types while preserving what was visible.
  if (typeof saved.stationMarkers !== "boolean")
    result.stationMarkers =
      typeof saved.stations === "boolean" ? saved.stations : true;
  if (typeof saved.industryMarkers !== "boolean")
    result.industryMarkers =
      (typeof saved.stations === "boolean" ? saved.stations : true) ||
      saved.industries === true;
  if (typeof saved.passengerStops !== "boolean")
    result.passengerStops = result.stationMarkers;
  return result;
}
function read(key) {
  try {
    const value = JSON.parse(readSetting(key) || "{}");
    return value && typeof value === "object" && !Array.isArray(value)
      ? value
      : {};
  } catch (error) {
    console.warn("ADS_LAYER_SETTINGS_RESET", error.name);
    return {};
  }
}
export function readLayers() {
  return normalizeLayers(read("ads.layers"));
}
export function saveLayers(layers) {
  // Preserve old keys for compatibility with an older installed frontend.
  saveSetting(
    "ads.layers",
    JSON.stringify({ ...read("ads.layers"), ...layers }),
  );
}
export function groupState(layers, children) {
  const keys = children
    .map(([key]) => key)
    .filter((key) => typeof layers[key] === "boolean");
  const count = keys.filter((key) => layers[key]).length;
  return {
    keys,
    checked: keys.length > 0 && count === keys.length,
    mixed: count > 0 && count < keys.length,
  };
}
export function setGroup(layers, children, checked) {
  for (const key of groupState(layers, children).keys) layers[key] = checked;
}
export function renderLayers(ui, root) {
  const open = (ui.layerGroupsOpen ??= read("ads.layerGroups"));
  root.replaceChildren(el("p", t("layerSettingsHint"), "layer-hint"));
  if (ui.renderer.focusRouteId)
    root.append(el("p", t("layerFocusHint"), "warning"));
  const onChanged = () => {
    saveLayers(ui.renderer.layers);
    ui.renderer.invalidate();
    ui.signalSubtabs();
    ui.renderInspector();
  };
  for (const group of layerGroups) {
    const state = groupState(ui.renderer.layers, group.children);
    if (!state.keys.length) continue;
    const details = el("details", undefined, "layer-group"),
      summary = el("summary"),
      parent = el("input");
    details.dataset.layerGroup = group.id;
    details.open =
      open[group.id] ?? ["signals", "junctions", "dispatch"].includes(group.id);
    parent.type = "checkbox";
    parent.dataset.layerParent = group.id;
    parent.setAttribute(
      "aria-label",
      t("layerWholeGroup") + ": " + t(group.title),
    );
    const updateParent = () => {
      const state = groupState(ui.renderer.layers, group.children);
      parent.checked = state.checked;
      parent.indeterminate = state.mixed;
      parent.setAttribute(
        "aria-checked",
        state.mixed ? "mixed" : String(state.checked),
      );
    };
    updateParent();
    summary.append(el("span", t(group.title)), parent);
    details.append(summary);
    parent.onclick = (event) => event.stopPropagation();
    parent.onchange = () => {
      setGroup(ui.renderer.layers, group.children, parent.checked);
      for (const input of details.querySelectorAll("[data-layer]"))
        input.checked = ui.renderer.layers[input.dataset.layer];
      updateParent();
      onChanged();
    };
    details.ontoggle = () => {
      open[group.id] = details.open;
      saveSetting("ads.layerGroups", JSON.stringify(open));
    };
    for (const [key, labelKey] of group.children) {
      if (!state.keys.includes(key)) continue;
      const label = el("label"),
        input = el("input");
      input.type = "checkbox";
      input.dataset.layer = key;
      input.checked = ui.renderer.layers[key];
      input.onchange = () => {
        ui.renderer.layers[key] = input.checked;
        updateParent();
        onChanged();
      };
      label.append(input, el("span", t(labelKey)));
      details.append(label);
    }
    root.append(details);
  }
}
