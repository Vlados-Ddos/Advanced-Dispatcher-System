import { isWagon } from "./rolling-stock.js";
import { t, number } from "./localization.js";
import { matchesSearch } from "./entity-search.js";

export function wagonGroup(store, id) {
  const cached = store.wagonGroupCache?.get(id);
  if (cached) {
    const first = store.cars.get(cached.head);
    return first ? { ...first, ...cached } : null;
  }
  const cars = [...(store.consists.get(id) || [])]
    .map((id) => store.cars.get(id))
    .filter((c) => isWagon(c) && c.nativeTrainset === true)
    .sort(
      (a, b) => (a.order ?? 0) - (b.order ?? 0) || a.id.localeCompare(b.id),
    );
  if (!cars.length) return null;
  const first = cars[0];
  const summary = {
    id,
    consist: id,
    kind: "wagonGroups",
    locomotive: false,
    count: cars.length,
    carIds: cars.map((c) => c.id),
    head: first.id,
    length: cars.every((c) => Number.isFinite(c.length) && c.length > 0)
      ? cars.reduce((n, c) => n + c.length, 0)
      : null,
  };
  store.wagonGroupCache?.set(id, summary);
  return { ...first, ...summary };
}
export function wagonGroupSummary(store, item) {
  const jobs = new Set(
    (item.carIds || []).map((id) => store.cars.get(id)?.job).filter(Boolean),
  );
  return (
    number(item.count) +
    " " +
    t("wagonsShort") +
    (Number.isFinite(item.length)
      ? " · " + number(item.length, 1) + " " + t("meters")
      : "") +
    (jobs.size ? " · " + [...jobs].join(", ") : "")
  );
}
// Every wagon occurs once in the flattened virtual list. Expansion is keyed
// by the native trainset ID; filtering never invents a different membership.
export function wagonRows(
  store,
  ordered,
  expanded,
  query,
  searchCollapsed = new Set(),
) {
  const groups = new Map();
  for (const car of ordered) {
    if (car.nativeTrainset !== true || !car.consist) continue;
    if (!groups.has(car.consist)) groups.set(car.consist, []);
    groups.get(car.consist).push(car);
  }
  const rows = [],
    included = new Set(),
    matched = new Set();
  let wagons = 0,
    groupCount = 0;
  for (const car of ordered) {
    if (included.has(car.id)) continue;
    const members =
      car.nativeTrainset === true ? groups.get(car.consist) : null;
    if (!members || members.length < 2) {
      if (matchesSearch(store, "cars", car, query)) {
        rows.push(car);
        wagons++;
        matched.add(car.id);
      }
      included.add(car.id);
      continue;
    }
    for (const member of members) included.add(member.id);
    const group = wagonGroup(store, car.consist),
      groupMatch = matchesSearch(
        store,
        "wagonGroups",
        { id: group.id, name: t("wagonGroup"), carIds: [] },
        query,
      );
    const matches = groupMatch
      ? members
      : members.filter((c) => matchesSearch(store, "cars", c, query));
    if (!matches.length) continue;
    rows.push({ ...group, matchingCount: matches.length });
    wagons += matches.length;
    groupCount++;
    for (const member of matches) matched.add(member.id);
    if (
      !searchCollapsed.has(group.id) &&
      (expanded.has(group.id) || query.trim())
    )
      for (const member of matches) rows.push({ ...member, wagonChild: true });
  }
  for (const id of expanded) if (!store.consists.has(id)) expanded.delete(id);
  return { rows, wagons, groups: groupCount, matched };
}
