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
  if (status !== "ready" && status !== "absent")
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
      ui.button(root, "parentLocation", () => {
        if(ui.navigationStack.at(-1)?.selection.id===parent.id)ui.backDetails();
        else ui.pick("locations",parent);
      });
  }
  const rows = (location.stationTracks || [])
    .filter((row) => row && typeof row.id === "string" && ui.store.tracks.has(row.id))
    .slice()
    .sort((a, b) => String(a.group || "other").localeCompare(String(b.group || "other"), language(), { numeric: true }) ||
      String(a.name || a.fullName || a.id).localeCompare(String(b.name || b.fullName || b.id), language(), { numeric: true }));
  const stops = passengerStop(location) ? [] : allLocations(ui.store).filter(item =>
    passengerStop(item) && item.id !== location.id && item.parent === location.id);
  // Preserve the semantic Passenger Jobs view for older/incomplete captures
  // that have platform children but no native track metadata yet.
  if (!rows.length && stops.length) {
    const stopGroups = new Map();
    for (const stop of stops) {
      const key = stop.trackGroup || "other";
      if (!stopGroups.has(key)) stopGroups.set(key, []);
      stopGroups.get(key).push(stop);
    }
    for (const [key, entries] of [...stopGroups.entries()].sort((a, b) => String(a[0]).localeCompare(String(b[0]), language(), { numeric: true }))) {
      const section = el("details", undefined, "location-stop-group");
      section.dataset.key = "location-stop-group-" + key;
      section.open = false;
      section.append(el("summary", (key === "other" ? t("otherLocations") : t("platformGroup") + " " + key) + " · " + entries.length));
      for (const stop of entries) {
        const button = el("button", locationName(ui.store, stop));
        button.dataset.key = "location-stop-" + stop.id;
        button.onclick = () => ui.pickNested("locations", stop);
        section.append(button);
      }
      root.append(section);
    }
    return;
  }
  const groups = new Map();
  for (const row of rows) {
    const key = row.group || "other";
    if (!groups.has(key)) groups.set(key, []);
    groups.get(key).push(row);
  }
  // Older captures may not yet carry stationTracks. Keep the same resolver
  // path for those records, but never invent a group from a label.
  if (!rows.length) for (const id of location.tracks || []) {
    const track = ui.store.tracks.get(id);
    if (!track) continue;
    if (!groups.has("other")) groups.set("other", []);
    groups.get("other").push({ id, name: trackName(ui.store, track), fullName: track.name, group: "other" });
  }
  for (const [key, entries] of [...groups.entries()].sort((a, b) => String(a[0]).localeCompare(String(b[0]), language(), { numeric: true }))) {
    const section = el("details", undefined, "location-stop-group");
    section.dataset.key = "location-track-group-" + key;
    section.open = false;
    section.append(el("summary", (key === "other" ? t("otherLocations") : t("platformGroup") + " " + key) + " · " + entries.length));
    for (const row of entries) {
      const track = ui.store.tracks.get(row.id);
      if (!track) continue;
      const label = row.name || row.fullName || trackName(ui.store, track);
      const button = el("button", label + (row.fullName && row.fullName !== label ? " · " + row.fullName : ""));
      button.dataset.key = "location-track-" + row.id;
      button.onclick = () => ui.pickNested("tracks", track);
      section.append(button);
    }
    root.append(section);
  }
  // Passenger platform locations remain useful as nested semantic stops when
  // the game exposes them, but they are secondary to the authoritative track
  // groups above and never replace or hide a real station track.
  if (stops.length) {
    const section = el("details", undefined, "location-stop-group");
    section.dataset.key = "location-passenger-stops";
    section.append(el("summary", t("passengerStops") + " · " + stops.length));
    for (const stop of stops) {
      const button = el("button", locationName(ui.store, stop));
      button.dataset.key = "location-stop-" + stop.id;
      button.onclick = () => ui.pickNested("locations", stop);
      section.append(button);
    }
    root.append(section);
  }
}
