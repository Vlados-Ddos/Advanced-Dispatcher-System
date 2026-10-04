import {
  t,
  number,
  dictionary,
  displayName,
  language,
} from "./localization.js";

// Presentation only: IDs remain untouched in stores, commands and navigation.
export function usableName(value) {
  return (
    typeof value === "string" &&
    value.trim() !== "" &&
    !/^(?:\[?\s*missing(?:[ _]+(?:translation|name))?\s*\]?|null|undefined|unknown|none)$/i.test(
      value.trim(),
    ) &&
    !/^(?:passjobs|w3)\//i.test(value)
  );
}
export function localizedValue(value, fallback = "valueUnavailable") {
  return value && Object.hasOwn(dictionary, value) ? t(value) : t(fallback);
}
function humanName(value) {
  return (
    usableName(value) &&
    !/^(?:pj:|sign:|tt:|b:)|^[tsj]\d+$|^T_[A-Z]+_\d+$|^[A-Z0-9]+(?:_[A-Z0-9]+)+$|^[0-9a-f]{8}-[0-9a-f-]{27,}$/i.test(
      value,
    )
  );
}
export function locationRecord(store, id) {
  return (
    store?.locations?.get(id) ||
    store?.locations?.get("pj:" + id) ||
    store?.topology?.stations?.find((s) => s.id === id)
  );
}
export function locationName(store, value) {
  const item =
    typeof value === "string"
      ? locationRecord(store, value) || { id: value }
      : value;
  if (!item) return t("locationUnavailable");
  const parent =
    item.parent && item.parent !== item.id
      ? locationRecord(store, item.parent)
      : null;
  const localized = record => {
    const value=language()==="ru"?record?.nameRu:record?.nameEn;
    return usableName(value)?value:record?.name;
  };
  const name=localized(item),parentName=localized(parent);
  const base = usableName(parentName) ? parentName : null;
  if (item.type === "passengerPlatform") {
    const code =
      (usableName(item.platformLabel) ? item.platformLabel : null) ||
      platformCode(item.platform || item.id?.replace(/^pj:platform:/, ""));
    return [
      t("platformName") + (code ? " " + code : ""),
      base || (humanName(name) ? name : null),
    ]
      .filter(Boolean)
      .join(" · ");
  }
  if (humanName(name)) return name;
  if (base) return base + " · " + t("passengerStopName");
  const code = item.code || /^(?:pj:)?([A-Z]{1,6})$/.exec(item.id || "")?.[1];
  return [
    t(item.type === "passengerRural" ? "passengerStopName" : "stationName"),
    code || reference(item),
  ].join(" ");
}
function platformCode(value) {
  if (/^[A-Z][A-Z0-9]*-\d+-(?:L|S|I|LP|SP|P)$/.test(value || "")) return value;
  // Passenger Jobs RouteTrack.PlatformID uses Track.ID.ToString() for yard platforms.
  // TrackID.ToString is [Y]_[yard]_[track]; L/S/I suffixes are native too.
  const yard = /^\[Y\]_\[[A-Z0-9]+\]_\[([A-Z][A-Z0-9]*-\d+-(?:L|S|I|LP|SP|P))\]$/.exec(
    value || "",
  );
  if (yard) return yard[1];
  const match =
    /^([A-Z0-9]+)[ _-]([A-Z])[-_]?(\d+)(?:[-_]?(?:L|S|I|LP|SP|P))?$/.exec(
      value || "",
    );
  return match
    ? match[2] + Number(match[3])
    : /^[A-Z]\d+$/.test(value || "")
      ? value
      : "";
}
function reference(item) {
  const numeric = /^(?:[tsj]|tt:t|b:s|b:t)(\d+)$/.exec(item?.id || "");
  if (numeric) return number(Number(numeric[1]));
  // A stable display reference for unnamed third-party entities, not a new ID.
  let hash = 2166136261;
  for (const c of String(item?.id || ""))
    hash = Math.imul(hash ^ c.charCodeAt(0), 16777619);
  return number(hash >>> 0);
}
export function trackName(store, value) {
  const item =
    typeof value === "string"
      ? store?.tracks?.get(value) || { id: value }
      : value;
  if (!item) return t("trackUnavailable");
  const name = item.name || "";
  const yard = /^\[Y#?\]_\[([^\]]+)\]_\[([^\]]+)\]$/i.exec(name);
  if (yard) return yard[1] + " " + yard[2]; // Actual yard/track designation.
  const road = /^\[#\]\s*Road\s+(\d+)$/i.exec(name);
  if (road) return t("track") + " " + number(Number(road[1]));
  const generated = /^\[track (through|diverging)\]$/i.exec(name);
  if (generated)
    return (
      t(
        generated[1].toLowerCase() === "through"
          ? "throughTrack"
          : "divergingTrack",
      ) +
      " " +
      reference(item)
    );
  return humanName(name)
    ? displayName(name)
    : t("track") + " " + reference(item);
}
export function vehicleModel(car) {
  return usableName(car?.model) &&
    (!car.modelLanguage ||
      car.modelLanguage === language() ||
      /^[A-Z][A-Z0-9]*(?:-\d+[A-Z]?)?$/.test(car.model))
    ? car.model
    : t("modelUnavailable");
}
export function entityName(store, kind, value) {
  let item = value;
  if (typeof value === "string") {
    item =
      kind === "wagonGroups"
        ? store?.wagonGroup?.(value)
        : kind === "trains"
          ? store?.train?.(value)
          : kind === "routes"
            ? store?.routes?.find((r) => r.id === value)
            : kind === "turntables"
              ? store?.tableDefs?.get(value)
              : kind === "switches"
                ? store?.junctions?.get(value)
                : store?.[kind]?.get?.(value);
    item ||= { id: value };
  }
  if (!item) return "—";
  if (kind === "wagonGroups") {
    const cars = (item.carIds || [])
      .map((id) => store.cars.get(id))
      .filter(Boolean);
    const first = cars[0],
      last = cars.at(-1);
    return (
      t("wagonGroup") +
      (first
        ? " · " +
          entityName(store, "cars", first) +
          (last !== first ? " — " + entityName(store, "cars", last) : "")
        : "")
    );
  }
  if (kind === "players" && item.id === "local" && item.name === "Player")
    return t("playerName");
  if (kind === "locations") return locationName(store, item);
  if (kind === "tracks") return trackName(store, item);
  if (kind === "signs" && item.signalObject)
    return [
      localizedValue(item.signKind || "fixedBoard", "railwaySign"),
      item.text,
      item.track ? trackName(store, item.track) : "",
      Number.isFinite(item.span)
        ? number(item.span) + " " + t("meters")
        : Number.isFinite(item.x) && Number.isFinite(item.z)
          ? t("mapPosition") +
            " " +
            number(item.x, 1) +
            "; " +
            number(item.z, 1)
          : "",
      item.name && humanName(item.name) ? item.name : "",
    ]
      .filter(Boolean)
      .join(" · ");
  if (kind === "signs") {
    const speeds =
      item.speeds?.filter((v) => Number.isFinite(v) && v > 0) || [];
    return [
      t(
        speeds.length
          ? item.advance
            ? "advanceSpeedSign"
            : "speedSign"
          : "railwaySign",
      ),
      speeds.length
        ? speeds.map((v) => number(v)).join(" / ") + " " + t("kmh")
        : "",
      trackName(store, item.track),
      Number.isFinite(item.span) ? number(item.span) + " " + t("meters") : "",
      t(item.direction > 0 ? "alongTrack" : "againstTrack"),
    ]
      .filter(Boolean)
      .join(" · ");
  }
  if (kind === "turntables") {
    const match = /^(?:Turntable )?T_([A-Z]+)_(\d+)$/.exec(item.name || "");
    if (match)
      return (
        t("turntableName") +
        " " +
        number(Number(match[2])) +
        " · " +
        locationName(store, match[1])
      );
  }
  if (kind === "blocks" && item.source === "dispatch")
    return t("section") + " · " + trackName(store, item.tracks?.[0] || item);
  if (kind === "log") return localizedValue(item.code, "eventRecorded");
  if(kind==="routes" && item.passengerRoute) {
    const job=store?.jobs?.get(item.jobId);
    const leg=job?.legs?.find(l=>l.passengerStop&&l.toTrack===item.to);
    const stop=locationRecord(store,leg?.station)||[...(store?.locations?.values()||[])].find(s=>s.passenger&&s.tracks?.includes(item.to));
    const destination=stop?locationName(store,stop)+(stop.code?" ["+stop.code+"]":""):t("passengerStopName");
    return trackName(store,item.from)+" → "+destination;
  }
  if (
    kind === "routes" &&
    item.from &&
    item.to &&
    /\[Y\]|\[track |^t\d+.*→|pj:|^(?:Consist|Train|Состав)\s+[\d\s,.\u00a0\u202f]+\s*→|^train:-?\d+.*→/i.test(item.name || "")
  )
    return (
      trackName(store, item.from) +
      " → " +
      trackName(store, item.to)
    );
  if (humanName(item.name)) {
    const name = displayName(item.name);
    return ["cars", "trains"].includes(kind) &&
      (item.locomotive || item.catalogColor) &&
      usableName(item.model)
      ? name + " · " + vehicleModel(item)
      : name;
  }
  if (kind === "jobs" && usableName(item.id)) return item.id; // Printed job number.
  if (kind === "routes" && item.from && item.to)
    return trackName(store, item.from) + " → " + trackName(store, item.to);
  return (
    t(
      {
        signals: "signalName",
        switches: "switchName",
        blocks: "section",
        cars: "carName",
        trains: "train",
        players: "playerName",
        turntables: "turntableName",
        routes: "routeName",
      }[kind] || "locationName",
    ) +
    " " +
    reference(item)
  );
}
export function targetName(store, id) {
  if (!id) return "—";
  for (const kind of [
    "cars",
    "signals",
    "tracks",
    "blocks",
    "jobs",
    "locations",
    "players",
    "signs",
  ])
    if (store[kind]?.has(id)) return entityName(store, kind, id);
  if (store.tableDefs?.has(id)) return entityName(store, "turntables", id);
  if (store.junctions?.has(id)) return entityName(store, "switches", id);
  if (store.routes?.some((r) => r.id === id))
    return entityName(store, "routes", id);
  if (store.train?.(id)) return entityName(store, "trains", id);
  return t("targetUnavailable");
}
