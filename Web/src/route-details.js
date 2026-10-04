import { entityName, trackName } from "./display-names.js";
import { el } from "./dom.js";
import { t, number, language } from "./localization.js";
import { detailsBack } from "./details-navigation.js";

export function routeMode(ui, root, route) {
  ui.kv(root, "status", t(route.status));
  ui.kv(
    root,
    "reservationMode",
    t("reservation_" + (route.reservationMode || "none")),
  );
  ui.kv(root, "reservation", t(route.reservationState || "none"));
  if (route.staged) {
    ui.kv(root, "routeStage", `${t("stage") } ${number(route.stageIndex + 1)} / ${number(route.stages?.length || 0)} · ${t("stage_" + (route.stageStatus || "active"))}`);
    ui.kv(root, "currentStage", `${trackName(ui.store, route.activeFrom || route.from)} → ${trackName(ui.store, route.activeTo || route.to)}`);
    const next = route.stages?.[route.stageIndex + 1];
    if (next) ui.kv(root, "nextStage", `${trackName(ui.store, next.from)} → ${trackName(ui.store, next.to)}`);
  }
  if (route.reason || route.invalidationReason) ui.kv(root, "reason", t(route.reason || route.invalidationReason));
  ui.kv(root, "routeOwner", route.owner === 'local-owner' ? t('localOwner') : route.owner);
  if(route.assignedPlayerKey) {
    const online=[...ui.store.players.values()].find(p=>p.identityKey===route.assignedPlayerKey);
    ui.kv(root,'assignedPlayer',online?entityName(ui.store,'players',online):route.assignedPlayerName || t('ownerUnavailable'));
  }
  if (route.recalculationState === "preview" && route.editPreview) {
    root.append(el("h3",t("routeEditPreview")));
    if (route.editPreview.affectedTrack)
      ui.kv(root, "track", entityName(ui.store, "tracks", route.editPreview.affectedTrack));
  }
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
            : route.reservationState === 'reserved' ? "normalRouteHint" : "unreservedRouteHint",
      ),
    ),
  );
}

export function pickWarning(ui, route, warning) {
  if(ui.selected)ui.pushDetailsContext();
  ui.detailNavigation=true;
  try {ui.renderer.select({ kind: warning.kind, id: warning.target });} finally {ui.detailNavigation=false;}
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
  const back = detailsBack(ui, "warnings", () => {
    const route = ui.store.routes.find(r => r.id === warning.route);
    ui.selected = route ? {kind:"routes",id:route.id} : null;
    ui.renderer.selected=ui.selected;
    ui.renderer.interactionDirty=true;
    if (route && ui.selected?.kind === "routes") {
      ui.routeDetailTabs ||= new Map();
      ui.routeDetailTabs.set(route.id, warning.active ? "warnings" : "resolved");
    }
    ui.renderInspector();
  });
  back.dataset.key = "warning-back";
  root.append(back);
  const status = el("section", undefined, "detail-section detail-status");
  status.append(el("h3", t("status")));
  ui.kv(status, "reason", t(warning.code));
  ui.kv(status, "status", t(warning.known === false ? "warningUnavailable" : warning.active ? "warningActive" : "warningResolved"));
  ui.kv(status, "eventTime", warning.firstSeen ? new Date(warning.firstSeen).toLocaleString(language()) : "—");
  if (warning.resolvedAt) ui.kv(status, "resolvedAt", new Date(warning.resolvedAt).toLocaleString(language()));
  root.append(status);
  const route = ui.store.routes.find((r) => r.id === warning.route);
  const related = el("section", undefined, "detail-section detail-related");
  related.append(el("h3", t("relatedObjects")));
  const shown = new Set();
  for (const [kind, id] of [
    ["routes", warning.route],
    [warning.kind, warning.target],
    ["trains", warning.train || route?.train],
    ["tracks", warning.track],
  ]) {
    if (!id || shown.has(kind + ":" + id)) continue;
    shown.add(kind + ":" + id);
    const obj = ui.renderer.resolve({ kind, id });
    const button = el(
      "button",
      t(kind) + ": " + entityName(ui.store, kind, obj || id),
    );
    button.disabled = !obj;
    button.onclick = () => {
      if (obj) ui.pickNested(kind, obj);
    };
    button.classList.add("related-object");
    related.append(button);
  }
  root.append(related);
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
  if (route.recalculationState === "unconfirmed") {
    root.append(el("p", t("ROUTE_EDIT_UNCONFIRMED"), "warning"));
    ui.button(root, "cancelRoute", () => ui.command("cancelRoute", {target:route.id}), true);
    return;
  }
  if (["recalculating", "replacing"].includes(route.recalculationState) || route.lifecycle === "preparing") {
    root.append(el("p", t(route.recalculationState === "replacing" ? "confirmingRouteEdit" : "planningRoute")));
    return;
  }
  if (route.staged && route.stageStatus === "available" && route.reservationState !== "reserved") {
    root.append(el("p", t("stagedNextLegHint"), "integration-note"));
    for (const mode of [route.stagedReservationMode || "normal", "protected"].filter((v, i, a) => a.indexOf(v) === i)) {
      const button = ui.button(root, "planNextStage", () => ui.command("advanceRouteStage", { target: route.id, reservationMode: mode }), true,
        mode !== "protected" || ui.store.capabilities.protectedReservations === true);
      button.textContent = t("planNextStage") + " · " + t("reservation_" + mode);
    }
    return;
  }
  if (route.recalculationState === "preview" && route.editPreview) {
    ui.button(
      root,
      "confirmRouteEdit",
      () => ui.command("confirmRouteEdit", { target: route.id, routeEditId: route.editPreview.id }),
      true,
    );
    ui.button(root, "cancelRouteEdit", () => ui.command("cancelRouteEdit", { target: route.id, routeEditId: route.editPreview.id }), true);
    return;
  }
  const overlaps = (route.conflicts || []).filter(c => c.code === "ROUTE_OVERLAP");
  if (overlaps.length) {
    const section = el("section", undefined, "detail-section reservation-conflict");
    section.append(el("h3", t("reservationConflict")));
    for (const conflict of overlaps) {
      const other = ui.store.routes.find(r => r.id === conflict.target);
      const text = [
        other ? entityName(ui.store, "routes", other) : conflict.target,
        other?.owner || other?.reservationOwner,
        other?.reservationMode ? t("reservation_" + other.reservationMode) : null,
        conflict.track ? trackName(ui.store, conflict.track) : null,
      ].filter(Boolean).join(" · ");
      section.append(el("p", text));
      const path = [...new Set([
        ...(other?.tracks || []),
        ...(other?.reservationTracks || []),
      ])].map(id => trackName(ui.store, id)).filter(Boolean).join(" → ");
      if (path) section.append(el("small", t("tracks") + ": " + path, "reservation-conflict-path"));
    }
    root.append(section);
  }
  if (!['reserved','releaseFailed'].includes(route.reservationState)) {
    const players = [...ui.store.players.values()].filter(p => p.identityKey);
    if (players.length) {
      const select = el("select");
      select.dataset.key = "assign-route-player";
      select.dataset.preserveValue = "true";
      select.setAttribute("aria-label", t("assignPlayer"));
      for (const player of players) {
        const option = el("option", player.name || player.id);
        option.value = player.id;
        option.selected = player.identityKey === route.assignedPlayerKey;
        select.append(option);
      }
      root.append(select);
      ui.button(root, "assignRoute", () =>
        ui.command("assignRoute", {target: route.id, action: document.querySelector('#inspector [data-key="assign-route-player"]')?.value}), true);
      if (route.assignedPlayerKey)
        ui.button(root, "unassignRoute", () => ui.command("unassignRoute", {target: route.id}), true);
    }
  }
  if (route.canRecalculate === true)
    ui.button(root, "recalculateRoute", () => ui.recalculateRoute(route), true);
  if (route.reservationState !== "reserved" && route.conflicts?.some(c => ["SWITCH_MISALIGNED", "TURNTABLE_MISALIGNED"].includes(c.code))) ui.button(
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
    const reserve = async (mode) => {
      const result = await ui.command("reserveRoute", { target: route.id, reservationMode: mode });
      // A normal reservation may cross an existing route only after an
      // explicit dispatcher confirmation.  Protected native reservations
      // never use this confirmation path.
      if (mode === "normal" && result?.code === "ROUTE_OVERLAP") {
        const force = await ui.confirmAction("ROUTE_OVERLAP_CONFIRM", "forceReservation");
        if (force) await ui.command("reserveRoute", { target: route.id, reservationMode: mode, forceReservation: true });
      }
      return result;
    };
    for (const mode of ["normal", "protected"])
      ui.button(
        root,
        "reserve_" + mode,
        () => reserve(mode),
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
