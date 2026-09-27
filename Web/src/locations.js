import { matchesSearch, normalizeSearch } from "./entity-search.js";
import { destinationColor } from "./job-display.js";
import { readSetting, saveSetting } from "./storage.js";
import { $, el, syncChildren } from "./dom.js";
import { locationName, trackName } from "./display-names.js";
import { t, language } from "./localization.js";

export function allLocations(store) {
  const result = new Map(
    (store.topology?.stations || []).map((s) => [s.id, s]),
  );
  for (const s of store.locations?.values() || []) {
    const base = result.get(s.id);
    result.set(
      s.id,
      base
        ? {
            ...base,
            ...s,
            city: base.city || s.city,
            industry: base.industry || s.industry,
            passenger: s.passenger || base.passenger,
            tracks: [...new Set([...(base.tracks || []), ...(s.tracks || [])])],
          }
        : s,
    );
  }
  return [...result.values()];
}
export function passengerStop(location) {
  return ["passengerPlatform", "passengerRural"].includes(location.type);
}
export function locationVisible(location, layers) {
  return passengerStop(location)
    ? !!layers.passengerStops
    : location.industry
      ? !!layers.industryMarkers
      : !!layers.stationMarkers;
}
export function locationCategory(location) {
  return passengerStop(location)
    ? "passengerStops"
    : location.city
      ? "cities"
      : location.industry
        ? "productions"
        : "otherLocations";
}
export function renderLocations(ui) {
  const root = $("location-list");
  root.hidden = ui.tab !== "locations";
  if (root.hidden) return;
  const fragment = document.createDocumentFragment();
  const all = allLocations(ui.store),
    query = normalizeSearch(ui.query);
  const items = all
    .filter((s) => matchesSearch(ui.store, "locations", s, query))
    .sort((a, b) =>
      locationName(ui.store, a).localeCompare(
        locationName(ui.store, b),
        language(),
        { numeric: true },
      ),
    );
  const state = ui.locationGroups || (ui.locationGroups = readGroups());
  const group = (id, name, members, children = false) => {
    const details = el("details", undefined, "location-group");
    details.dataset.key = "location-group-" + id;
    details.dataset.controlledOpen = "true";
    details.open = query ? true : state[id] !== false;
    details.ontoggle = (e) => {
      if (ui.query) return;
      state[id] = e.currentTarget.open;
      try {
        saveSetting("ads.locationGroups", JSON.stringify(state));
      } catch {
        /* Browsing remains usable without storage. */
      }
    };
    details.append(el("summary", name + " · " + members.length));
    if (!members.length) details.append(el("small", t("noLocations")));
    if (children) {
      const parents = new Map();
      for (const item of members) {
        const key = item.type === "passengerRural" ? "ruralStops" : item.parent;
        if (!parents.has(key)) parents.set(key, []);
        parents.get(key).push(item);
      }
      for (const [key, entries] of parents)
        details.append(
          group(
            id + ":" + key,
            key === "ruralStops" ? t(key) : locationName(ui.store, key),
            entries,
          ),
        );
    } else
      for (const item of members) {
        const button = el("button", undefined, "location-link");
        button.dataset.key = "location-" + item.id;
        button.style.borderLeft =
          "4px solid " + destinationColor(ui.store, item.id);
        button.append(
          el("strong", locationName(ui.store, item)),
          el(
            "small",
            t(locationCategory(item)) +
              (item.passenger && !passengerStop(item)
                ? " · " + t("hasPassengerPlatforms")
                : ""),
          ),
        );
        button.onclick = () => ui.pick("locations", item);
        button.title = locationName(ui.store, item);
        button.classList.toggle(
          "selected",
          ui.selected?.kind === "locations" && ui.selected.id === item.id,
        );
        details.append(button);
      }
    return details;
  };
  for (const key of [
    "productions",
    "cities",
    "passengerStops",
    "otherLocations",
  ])
    if (items.some((s) => locationCategory(s) === key))
      fragment.append(
        group(
          key,
          t(key),
          items.filter((s) => locationCategory(s) === key),
          key === "passengerStops",
        ),
      );
  if (!items.length) fragment.append(el("p", t("noLocations"), "empty-list"));
  const status = ui.store.capabilities.passengerStatus || "absent";
  if (status !== "ready")
    fragment.append(
      el("p", t("passengerStatus_" + status), "integration-note"),
    );
  $("list-count").textContent = String(items.length);
  syncChildren(root, fragment);
}
function readGroups() {
  try {
    const saved = JSON.parse(readSetting("ads.locationGroups") || "{}");
    return saved && typeof saved === "object" && !Array.isArray(saved)
      ? saved
      : {};
  } catch {
    return {};
  }
}
export function locationDetails(ui, root, location) {
  ui.kv(root, "type", t(locationCategory(location)));
  ui.kv(root, "source", location.source || "Derail Valley");
  if (location.passenger) ui.kv(root, "passengerStops", t("Available"));
  if (location.parent) {
    const parent = allLocations(ui.store).find(
      (s) => s.id === location.parent && s.id !== location.id,
    );
    if (parent)
      ui.button(root, "parentLocation", () => ui.pick("locations", parent));
  }
  for (const id of location.tracks || []) {
    const track = ui.store.tracks.get(id);
    if (!track) continue;
    const b = el("button", trackName(ui.store, track));
    b.dataset.key = "location-track-" + id;
    b.onclick = () => ui.pick("tracks", track);
    root.append(b);
  }
}
