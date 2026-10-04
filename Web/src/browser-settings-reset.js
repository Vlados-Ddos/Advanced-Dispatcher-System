import { el } from "./dom.js";
import { t } from "./localization.js";
import { resetStoredBrowserSettings, cancelBrowserSettingsReset } from "./storage.js";

export function resetBrowserSettings(reload = () => globalThis.location.reload()) {
  if (!resetStoredBrowserSettings()) return false;
  try {
    // A fresh application restores all defaults together, including transient
    // navigation, selection/follow state and camera state. Authentication stays.
    reload();
    return true;
  } catch {
    cancelBrowserSettingsReset();
    return false;
  }
}

export function browserResetControls(parent, reset = resetBrowserSettings) {
  const root = el("div", undefined, "browser-settings-reset"), start = el("button", t("resetBrowserSettings"));
  start.type = "button";
  start.id = "reset-browser-settings";
  const confirmation = el("div", undefined, "browser-reset-confirmation"), confirm = el("button", t("confirmBrowserReset"), "primary"), cancel = el("button", t("cancel")), status = el("p");
  confirmation.id = "browser-reset-confirmation";
  confirmation.hidden = true;
  confirmation.setAttribute("role", "group");
  confirmation.setAttribute("aria-label", t("resetBrowserSettings"));
  confirm.id = "confirm-browser-reset";
  cancel.id = "cancel-browser-reset";
  confirm.type = cancel.type = "button";
  status.id = "browser-reset-status";
  status.setAttribute("role", "status");
  const actions = el("div", undefined, "browser-reset-actions");
  actions.append(cancel, confirm);
  confirmation.append(el("p", t("browserResetScope")), actions, status);
  root.append(start, confirmation);
  parent.append(root);
  start.onclick = () => { start.hidden = true; confirmation.hidden = false; status.textContent = ""; confirm.focus(); };
  cancel.onclick = () => { confirmation.hidden = true; start.hidden = false; start.focus(); };
  confirm.onclick = () => {
    if (confirm.disabled) return;
    confirm.disabled = cancel.disabled = true;
    status.textContent = t("browserResetReloading");
    if (!reset()) {
      status.textContent = t("browserResetFailed");
      status.classList.add("account-error");
      confirm.disabled = cancel.disabled = false;
    }
  };
}
