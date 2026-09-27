import { entityName } from "./display-names.js";
import { el } from "./dom.js";
import { t, language } from "./localization.js";

export function routeMode(ui, root, route) {
  ui.kv(root, "status", t(route.status));
  ui.kv(
    root,
    "reservationMode",
    t("reservation_" + (route.reservationMode || "none")),
  );
  ui.kv(root, "reservation", t(route.reservationState || "none"));
  ui.kv(root, "routeOwner", route.owner);
  ui.kv(
    root,
    "eventTime",
    route.createdAt > 0
      ? new Date(route.createdAt).toLocaleString(language())
      : t("routePreviewTime"),
  );
  root.append(
    el(
      "p",
      t(
        route.endedAt
          ? "routeHistoryHint"
          : route.reservationMode === "protected" &&
              route.reservationState === "reserved"
            ? "protectedRouteHint"
            : "normalRouteHint",
      ),
    ),
  );
}

export function pickWarning(ui, route, warning) {
  ui.renderer.select({ kind: warning.kind, id: warning.target });
  ui.selected = {
    kind: "warnings",
    id: warning.id,
    route: route.id,
    warning: { ...warning },
  };
  ui.renderInspector();
  if (document.body.classList.contains("compact"))
    document.body.classList.add("collapsed");
}

export function currentWarning(ui) {
  const selection = ui.selected;
  const route = selection.route
    ? ui.store.routes.find((r) => r.id === selection.route)
    : ui.store.preview;
  if (!route) return null;
  const matches = (c) =>
    c.id === selection.id && c.firstSeen === selection.warning.firstSeen;
  const current = route?.conflicts?.find(matches);
  const historical = route?.history?.findLast(matches);
  return {
    ...(current || historical || selection.warning),
    route: selection.route,
    known: !!(current || historical),
    active: !!current && !route?.endedAt,
  };
}

export function warningDetails(ui, root, warning) {
  ui.kv(root, "reason", t(warning.code));
  ui.kv(
    root,
    "status",
    t(
      warning.known === false
        ? "warningUnavailable"
        : warning.active
          ? "warningActive"
          : "warningResolved",
    ),
  );
  ui.kv(
    root,
    "eventTime",
    warning.firstSeen
      ? new Date(warning.firstSeen).toLocaleString(language())
      : "—",
  );
  if (warning.resolvedAt)
    ui.kv(
      root,
      "resolvedAt",
      new Date(warning.resolvedAt).toLocaleString(language()),
    );
  const route = ui.store.routes.find((r) => r.id === warning.route);
  for (const [kind, id] of [
    ["routes", warning.route],
    [warning.kind, warning.target],
    ["trains", warning.train || route?.train],
    ["tracks", warning.track],
  ]) {
    if (!id) continue;
    const obj = ui.renderer.resolve({ kind, id });
    const button = el(
      "button",
      t(kind) + ": " + entityName(ui.store, kind, obj || id),
    );
    button.disabled = !obj;
    button.onclick = () => {
      if (obj) ui.pick(kind, obj);
    };
    root.append(button);
  }
}

export function reservationActions(ui, root, route) {
  if (route.endedAt) {
    if (route.reservationState === "releaseFailed") {
      root.append(
        el("p", t(route.reason || "SIGNALS_RELEASE_FAILED"), "warning"),
      );
      ui.button(
        root,
        "retryReleaseRoute",
        () => ui.command("releaseRoute", { target: route.id }),
        true,
      );
    }
    return;
  }
  const focus = ui.button(
    root,
    ui.renderer.focusRouteId === route.id ? "exitRouteFocus" : "showRouteFocus",
    () => {
      ui.renderer.toggleRouteFocus(route);
      ui.renderInspector();
    },
  );
  focus.dataset.key = "route-focus";
  focus.setAttribute(
    "aria-pressed",
    String(ui.renderer.focusRouteId === route.id),
  );
  ui.button(
    root,
    "applyRoute",
    () => ui.command("applyRoute", { target: route.id }),
    true,
  );
  if (["reserved", "releaseFailed"].includes(route.reservationState)) {
    ui.button(
      root,
      "releaseRoute",
      () => ui.command("releaseRoute", { target: route.id }),
      true,
    );
  } else {
    for (const mode of ["normal", "protected"])
      ui.button(
        root,
        "reserve_" + mode,
        () =>
          ui.command("reserveRoute", {
            target: route.id,
            reservationMode: mode,
          }),
        true,
        mode !== "protected" ||
          ui.store.capabilities.protectedReservations === true,
      );
  }
  ui.button(
    root,
    "completeRoute",
    () => ui.command("completeRoute", { target: route.id }),
    true,
  );
  ui.button(
    root,
    "cancelRoute",
    () => ui.command("cancelRoute", { target: route.id }),
    true,
  );
}

export function routeMatches(route, filter) {
  if (filter === "routeHistory") return !!route.endedAt;
  if (filter === "activeRoutes") return !route.endedAt;
  if (filter === "routeWarnings")
    return !route.endedAt && !!route.conflicts?.length;
  if (filter === "reservedRoutes")
    return !route.endedAt && route.reservationState === "reserved";
  return true;
}
