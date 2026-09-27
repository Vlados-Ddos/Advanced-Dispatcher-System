import { dictionary, displayName } from "./localization.js";
import { entityName, locationRecord } from "./display-names.js";
import { jobOwner } from "./job-groups.js";
import { aspectText } from "./signal-display.js";

export const normalizeSearch = (value) =>
  String(value ?? "")
    .normalize("NFKC")
    .toLocaleLowerCase("en-US")
    .replace(/ё/g, "е")
    .replace(/\s+/gu, " ")
    .trim();
// One reverse index over the existing RU/EN resources. Search never switches
// the UI language, fabricates translations, or uses localized values as IDs.
const aliases = new Map();
for (const [key, pair] of Object.entries(dictionary))
  for (const value of [key, ...pair]) {
    const normalized = normalizeSearch(value);
    const list = aliases.get(normalized) || new Set();
    for (const word of [key, ...pair]) list.add(word);
    aliases.set(normalized, list);
  }
function add(values, value) {
  if (Array.isArray(value)) {
    for (const entry of value) add(values, entry);
    return;
  }
  if (typeof value !== "string" && typeof value !== "number") return;
  const normal = normalizeSearch(value);
  if (!normal) return;
  values.push(normal);
  for (const alias of aliases.get(normal) || [])
    values.push(normalizeSearch(alias));
}
export function searchText(store, kind, item) {
  const values = [];
  for (const key of [
    "name",
    "id",
    "model",
    "searchNames",
    "type",
    "vehicleCategory",
    "consist",
    "job",
    "destination",
    "origin",
    "aspect",
    "mode",
    "state",
    "status",
    "cargo",
    "actor",
    "detail",
    "target",
    "code",
    "severity",
    "source",
    "platform",
    "platformLabel",
    "parent",
    "signKind",
    "types",
    "owner",
    "from",
    "to",
    "track",
    "track1",
    "track2",
    "tracks",
  ])
    add(values, item?.[key]);
  add(values, entityName(store, kind, item));
  add(values, displayName(item?.name));
  if (kind === "tracks") add(values, "track");
  if (kind === "signals") add(values, aspectText(item));
  add(values, item?.consistCargo?.types);
  if (kind === "trains" && item.locomotive) {
    const members = item.consistCargo?.membershipComplete
      ? item.consistCargo.members
      : [...(store.consists.get(item.consist) || [])];
    for (const id of members) {
      const car = store.cars.get(id);
      if (car) {
        add(values, car.job);
        if (car.cargoKnown && car.cargoAmount > 0) add(values, car.cargo);
      }
    }
  }
  add(values, kind === "trains" ? "locomotives" : kind);
  if (kind === "jobs") add(values, jobOwner(item, store).name);
  for (const id of [item?.parent, item?.origin, item?.destination]) {
    const place = locationRecord(store, id);
    add(values, place?.name);
    add(values, place?.searchNames);
  }
  if (kind === "locations")
    add(
      values,
      item.industry
        ? "productions"
        : item.city
          ? "cities"
          : ["passengerPlatform", "passengerRural"].includes(item.type)
            ? "passengerStops"
            : "otherLocations",
    );
  if (item?.occupied) add(values, "occupied");
  if (item?.reserved) add(values, "reserved");
  return values.join("\n");
}
export function matchesSearch(store, kind, item, query) {
  const normalized = normalizeSearch(query);
  if (!normalized) return true;
  const text = searchText(store, kind, item);
  return text.includes(normalized);
}
export const tabLabel = (kind) => (kind === "trains" ? "locomotives" : kind);
