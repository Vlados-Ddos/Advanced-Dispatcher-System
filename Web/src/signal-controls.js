import { $, el } from "./dom.js";
import { t, number } from "./localization.js";
import { localizedValue, trackName } from "./display-names.js";
import { signalCategory, aspectText } from "./signal-display.js";

export function renderSignalControls(ui, root, actions, item) {
  const id = item.id;
  const enabled =
    ui.store.canControl &&
    ui.store.capabilities.signalCommands === true &&
    ui.user?.role !== "viewer";
  const temporary =
    ui.signalModeDraft?.id === id &&
    ui.signalModeDraft.mode === item.mode &&
    ui.signalModeDraft.overrideIndex === item.overrideIndex;
  const commandMode = temporary ? "TempOverride" : item.mode;
  ui.kv(
    root,
    "signalCategory",
    t(
      signalCategory(item) === "shunting"
        ? "shuntingSignals"
        : signalCategory(item) === "standard"
          ? "signalsStandard"
          : "unknown",
    ),
  );
  ui.kv(root, "aspect", aspectText(item));
  ui.kv(root, "mode", localizedValue(item.mode));
  if (item.type)
    ui.kv(
      root,
      "signalControllerType",
      localizedValue("signalType_" + item.type),
    );
  ui.kv(root, "track", trackName(ui.store, item.track));
  ui.kv(root, "reservation", t(item.reserved ? "reserved" : "none"));
  const select = el("select");
  for (const mode of [
    "Automatic",
    "TempOverride",
    "SemiManual",
    "FullManual",
  ]) {
    const option = el("option", t(mode));
    option.value = mode;
    select.append(option);
  }
  select.value = commandMode;
  select.dataset.key = "signal-mode";
  select.dataset.mutating = "true";
  select.dataset.authoritative = "true";
  select.disabled = !enabled || ui.pendingCommands > 0;
  select.dataset.unavailable = String(!ui.store.capabilities.signalCommands);
  select.onchange = (e) => {
    const requested = e.currentTarget.value;
    e.currentTarget.value = item.mode;
    ui.signalModeDraft = null;
    if (requested === "TempOverride") {
      ui.signalModeDraft = {
        id,
        mode: item.mode,
        overrideIndex: item.overrideIndex,
      };
      ui.signalAspectDraft = null;
      ui.renderInspector();
      return;
    }
    ui.command("setSignalMode", {
      target: id,
      action: requested,
      expectedRevision: item.revision,
    });
  };
  const label = el("label", t("mode"));
  label.dataset.key = "signal-mode-label";
  label.append(select);
  root.append(
    label,
    el("p", t("signalModeHint_" + commandMode), "integration-note"),
  );
  if (temporary)
    root.append(el("p", t("temporaryDraftHint"), "integration-note"));
  ui.kv(
    root,
    "storedSignalOverride",
    item.overrideIndex >= 0 ? aspectText(item, item.overrideIndex) : t("off"),
  );
  const aspects = el("select");
  aspects.dataset.key = "signal-aspect";
  aspects.dataset.authoritative = "true";
  const draft =
    ui.signalAspectDraft?.id === id &&
    ui.signalAspectDraft.mode === commandMode &&
    ui.signalAspectDraft.count === item.aspectCount
      ? ui.signalAspectDraft.index
      : null;
  aspects.onchange = (e) => {
    ui.signalAspectDraft = {
      id,
      mode: commandMode,
      count: item.aspectCount,
      index: Number(e.currentTarget.value),
    };
    ui.renderInspector();
  };
  if (commandMode === "FullManual") {
    const off = el("option", t("off"));
    off.value = "-1";
    aspects.append(off);
  }
  for (let i = 0; i < item.aspectCount; i++) {
    const opt = el("option", i + 1 + " · " + aspectText(item, i));
    opt.value = i;
    aspects.append(opt);
  }
  const currentIndex =
    item.mode === "Automatic" ? item.aspectIndex : item.overrideIndex;
  if (currentIndex < 0 && draft === null && commandMode !== "FullManual") {
    const placeholder = el("option", t("off"));
    placeholder.value = "";
    placeholder.disabled = true;
    aspects.prepend(placeholder);
  }
  aspects.value =
    draft ??
    (currentIndex >= 0 || commandMode === "FullManual" ? currentIndex : "");
  const aspectLabel = el("label", t("commandAspect"), "aspect-command");
  aspectLabel.dataset.key = "signal-aspect-label";
  aspectLabel.append(
    aspects,
    el(
      "output",
      aspectText(item, draft ?? (currentIndex >= 0 ? currentIndex : -1)),
      "aspect-caption",
    ),
  );
  root.append(aspectLabel, el("small", t("manualAspectHint")));
  if (
    !item.off &&
    item.lampLayout?.some((l) => l.indicationKnown && l.indicated) &&
    !item.lampLayout.some((l) => l.on)
  )
    root.append(el("p", t("signalPhysicalDark")));
  ui.button(
    actions,
    "setAspect",
    async () => {
      const value = $("inspector").querySelector(
        '[data-key="signal-aspect"]',
      ).value;
      if (value === "") return;
      const result = await ui.command("setSignalAspect", {
        target: id,
        index: Number(value),
        ...(temporary ? { action: "TempOverride" } : {}),
        expectedRevision: item.revision,
      });
      if (result?.status === "applied") {
        if (ui.signalAspectDraft?.id === id) ui.signalAspectDraft = null;
        if (ui.signalModeDraft?.id === id) ui.signalModeDraft = null;
        if (ui.selected?.kind === "signals" && ui.selected.id === id)
          ui.renderInspector();
      }
    },
    true,
    enabled &&
      commandMode !== "Automatic" &&
      (draft !== null || currentIndex >= 0),
  );
  const shunt = el("input");
  shunt.type = "checkbox";
  shunt.checked = item.shunting;
  shunt.dataset.mutating = "true";
  shunt.dataset.authoritative = "true";
  shunt.disabled = select.disabled;
  shunt.dataset.unavailable = select.dataset.unavailable;
  shunt.onchange = (e) => {
    const value = e.currentTarget.checked;
    e.currentTarget.checked = item.shunting;
    ui.command("setShunting", {
      target: id,
      value: value ? 1 : 0,
      expectedRevision: item.revision,
    });
  };
  const sl = el("label", t("shunting"));
  sl.dataset.key = "signal-shunting-label";
  sl.append(shunt);
  root.append(sl);
  if (item.reserved)
    ui.kv(
      root,
      "reservationRemaining",
      item.reservationSeconds >= 0
        ? number(item.reservationSeconds) + " " + t("seconds")
        : t("reservationUnlimited"),
    );
  if (item.reservable !== true && !item.reserved) {
    root.append(el("p", t("signalCannotReserve"), "integration-note"));
    return;
  }
  const duration = el("input");
  duration.type = "number";
  duration.dataset.key = "signal-duration";
  duration.dataset.preserveValue = "true";
  duration.value = "120";
  duration.min = "0";
  duration.max = "3600";
  const dl = el("label", t("signalReservationDuration"));
  dl.dataset.key = "signal-duration-label";
  dl.append(duration);
  root.append(
    dl,
    el("p", t("signalReservationDurationHint"), "integration-note"),
  );
  ui.button(
    actions,
    item.reserved ? "cancelSignal" : "reserveSignal",
    () => {
      const input = $("inspector").querySelector(
        '[data-key="signal-duration"]',
      );
      const seconds = Number(input.value);
      if (
        !item.reserved &&
        (!input.value.trim() ||
          !Number.isFinite(seconds) ||
          seconds < 0 ||
          seconds > 3600)
      ) {
        ui.toast(t("INVALID_VALUE"), true);
        return;
      }
      return ui.command(
        item.reserved ? "cancelSignalReservation" : "reserveSignal",
        {
          target: id,
          value: item.reserved ? 0 : seconds,
          expectedRevision: item.revision,
        },
      );
    },
    true,
    enabled,
  );
}
