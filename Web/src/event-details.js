import { el } from "./dom.js";
import { t, language } from "./localization.js";
import { localizedValue, entityName, targetName } from "./display-names.js";

export function eventTarget(ui, event) {
  const id = event.target;
  if (!id) return null;
  const kinds = event.targetKind
    ? [event.targetKind]
    : [
        "blocks",
        "signals",
        "cars",
        "trains",
        "switches",
        "turntables",
        "routes",
        "jobs",
        "players",
        "tracks",
        "locations",
        "signs",
      ];
  const found = kinds
    .map((kind) => ({ kind, id, item: ui.renderer.resolve({ kind, id }) }))
    .filter((x) => x.item);
  return found.length === 1 ? found[0] : null;
}
export function eventTime(time) {
  return Number.isFinite(time) && time > 0
    ? new Date(time).toLocaleString(language())
    : t("eventTimeUnavailable");
}
export function eventSource(source) {
  return [
    "DV Signals",
    "Passenger Jobs",
    "Derail Valley",
    "Multiplayer",
  ].includes(source)
    ? source
    : localizedValue(
        ["dispatch", "dispatcher"].includes(source)
          ? "eventSourceDispatch"
          : source,
        "eventSourceUnavailable",
      );
}
export function renderEvent(ui, root, actions, event) {
  const target = eventTarget(ui, event);
  for (const key of ["type", "severity"])
    ui.kv(root, key, localizedValue(event[key]));
  ui.kv(root, "source", eventSource(event.source));
  ui.kv(
    root,
    "actor",
    event.actor === "local-owner"
      ? t("localOwner")
      : event.actor === "game" && event.type === "system"
        ? t("game")
        : event.actor || t("eventActorUnavailable"),
  );
  ui.kv(
    root,
    "target",
    target
      ? entityName(ui.store, target.kind, target.item)
      : targetName(ui.store, event.target),
  );
  let message = localizedValue(event.code, "eventRecorded");
  if (
    event.code === "blockChanged" &&
    ["occupied", "free", "unknown", "stale"].includes(event.detail)
  )
    message += " · " + localizedValue(event.detail);
  if (
    ["trainEnteredBlock", "trainLeftBlock"].includes(event.code) &&
    event.detail &&
    ui.store.train(event.detail)
  )
    message += " · " + entityName(ui.store, "trains", event.detail);
  ui.kv(root, "message", message);
  ui.kv(root, "eventTime", eventTime(event.time));
  const go = ui.button(
    actions,
    "goToEventObject",
    () => {
      const current = eventTarget(ui, event);
      if (current) {
        ui.setTab(current.kind);
        ui.pick(current.kind, current.item);
      } else ui.toast(t("targetUnavailable"));
    },
    false,
    !!target,
  );
  if (!target) {
    go.title = t("eventObjectUnavailable");
    root.append(el("small", t("eventObjectUnavailable")));
  }
}
