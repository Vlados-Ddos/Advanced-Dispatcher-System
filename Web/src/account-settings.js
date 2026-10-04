import { el } from "./dom.js";
import { t } from "./localization.js";

const accountErrors = new Set(["HOST_TIMEOUT", "HOST_UNAVAILABLE", "HOST_BAD_RESPONSE", "AUTH_REQUIRED", "FORBIDDEN", "INVALID_ACCOUNT", "ACCOUNT_EXISTS", "ACCOUNT_NOT_FOUND", "ACCOUNT_LIMIT", "RATE_LIMITED"]);
const validName = value => value.length > 0 && value.length <= 48 && value !== "local-owner" && /^[\p{L}\p{Nd}_-]+$/u.test(value) && !/[\uD800-\uDFFF]/.test(value);
const validPassword = value => value.length >= 10 && value.length <= 128 && value.trim().length > 0;

export function accountSettings(ui, parent) {
  const root = el("section", undefined, "account-profiles");
  root.id = "account-profiles";
  parent.append(root);
  const owner = ui.user;
  const current = () => root.isConnected && ui.user === owner;
  const state = { mode: "edit", selected: null, accounts: [], loaded: false, loading: false, saving: false, version: 0, confirmation: null };
  const button = (id, key, className) => {
    const node = el("button", t(key), className);
    node.id = id;
    node.type = "button";
    return node;
  };
  const heading = el("div", undefined, "account-heading"), count = el("span", "0", "account-count");
  heading.append(el("h4", t("accounts")), count);
  root.append(heading, el("p", t("profilesIntro"), "account-intro"));
  const modes = el("div", undefined, "account-modes"), editMode = button("account-mode-edit", "existingProfiles"), createMode = button("account-mode-create", "createProfile");
  modes.setAttribute("role", "tablist");
  modes.setAttribute("aria-label", t("accounts"));
  for (const node of [editMode, createMode]) { node.setAttribute("role", "tab"); node.setAttribute("aria-controls", "account-editor-panel"); }
  modes.append(editMode, createMode);
  root.append(modes);
  const panel = el("div");
  panel.id = "account-editor-panel";
  panel.setAttribute("role", "tabpanel");
  root.append(panel);

  const picker = el("div", undefined, "account-picker"), pickerLabel = el("label", t("selectProfile")), existing = el("select"), reload = button("reload-accounts", "reloadAccounts", "account-refresh");
  existing.id = "existing-accounts";
  pickerLabel.htmlFor = existing.id;
  pickerLabel.append(existing);
  picker.append(pickerLabel, reload);
  const listStatus = el("p", undefined, "account-status");
  listStatus.id = "accounts-status";
  listStatus.dataset.key = "accounts-status";
  listStatus.setAttribute("role", "status");
  const empty = el("div", undefined, "account-empty"), emptyCreate = button("account-empty-create", "createFirstProfile", "primary");
  empty.id = "account-empty";
  empty.append(el("strong", t("noProfiles")), el("p", t("noProfilesHint")), emptyCreate);
  const chooseHint = el("p", t("chooseProfileHint"), "account-status");
  panel.append(picker, listStatus, empty, chooseHint);

  const form = el("form", undefined, "account-form"), formHeading = el("h4"), selectedSummary = el("div", undefined, "account-selected-summary");
  form.id = "account-management-form";
  form.noValidate = true;
  selectedSummary.id = "account-selected-summary";
  const identity = el("fieldset", undefined, "account-fields"), nameLabel = el("label", t("profileName")), name = el("input"), nameHint = el("small", t("profileNameHint"));
  identity.append(el("legend", t("profileIdentity")));
  name.name = "name";
  name.id = "account-name-input";
  name.autocomplete = "off";
  name.maxLength = 48;
  nameLabel.htmlFor = name.id;
  nameHint.id = "account-name-hint";
  name.setAttribute("aria-describedby", nameHint.id + " account-form-status");
  nameLabel.append(name, nameHint);
  identity.append(nameLabel);
  const permissions = el("fieldset", undefined, "account-fields"), roleLabel = el("label", t("role")), role = el("select"), roleDescription = el("p", undefined, "account-role-description");
  permissions.append(el("legend", t("profilePermissions")));
  role.name = "role";
  role.id = "account-role";
  roleLabel.htmlFor = role.id;
  roleDescription.id = "account-role-description";
  role.setAttribute("aria-describedby", roleDescription.id);
  for (const value of ["viewer", "dispatcher"]) {
    const option = el("option", t(value)); option.value = value; role.append(option);
  }
  roleLabel.append(role);
  permissions.append(roleLabel, roleDescription);
  const security = el("fieldset", undefined, "account-fields"), changeLabel = el("label", undefined, "account-password-toggle"), changePassword = el("input"), passwordLabel = el("label", t("newProfilePassword")), password = el("input"), passwordHint = el("small", t("profilePasswordHint"));
  security.append(el("legend", t("profileSecurity")));
  changePassword.type = "checkbox";
  changePassword.name = "changePassword";
  changeLabel.append(changePassword, el("span", t("changeProfilePassword")));
  password.name = "password";
  password.id = "account-password-input";
  password.type = "password";
  password.minLength = 10;
  password.maxLength = 128;
  password.autocomplete = "new-password";
  passwordLabel.htmlFor = password.id;
  passwordHint.id = "account-password-hint";
  password.setAttribute("aria-describedby", passwordHint.id + " account-form-status");
  passwordLabel.append(password, passwordHint);
  const retained = el("small", t("profilePasswordUnchanged"));
  security.append(changeLabel, retained, passwordLabel);
  const status = el("p", undefined, "account-status"), submit = button("account-submit", "saveProfileChanges", "primary");
  status.id = "account-form-status";
  status.setAttribute("role", "status");
  submit.type = "submit";
  form.append(formHeading, selectedSummary, identity, permissions, security, el("small", t("profileSessionHint")), status, submit);
  panel.append(form);

  const danger = el("div", undefined, "account-danger"), remove = button("delete-account", "deleteAccount", "account-danger-button"), confirmation = el("div", undefined, "account-delete-confirmation"), confirmText = el("p"), confirm = button("confirm-delete-account", "confirmProfileDelete", "account-danger-button"), cancel = button("cancel-delete-account", "cancel");
  danger.id = "account-danger";
  confirmation.id = "account-delete-confirmation";
  confirmation.setAttribute("role", "group");
  confirmation.setAttribute("aria-label", t("confirmProfileDelete"));
  const dangerActions = el("div", undefined, "account-danger-actions");
  dangerActions.append(cancel, confirm);
  confirmation.append(confirmText, dangerActions);
  danger.append(el("strong", t("profileDeleteHeading")), el("p", t("profileDeleteHint")), remove, confirmation);
  panel.append(danger);

  function report(key = "", failed = false) {
    status.textContent = key ? t(key) : "";
    status.classList.toggle("account-error", failed);
    status.hidden = !key;
  }
  function resetDraft() {
    const account = state.accounts.find(item => item.name === state.selected);
    role.value = state.mode === "edit" ? account?.role || "viewer" : "viewer";
    name.value = state.mode === "create" ? "" : account?.name || "";
    password.value = "";
    changePassword.checked = false;
    name.removeAttribute("aria-invalid");
    password.removeAttribute("aria-invalid");
    report();
  }
  function options() {
    const placeholder = el("option", t("selectProfile")); placeholder.value = "";
    existing.replaceChildren(placeholder);
    for (const account of state.accounts) {
      const option = el("option", account.name + " · " + t(account.role)); option.value = account.name; existing.append(option);
    }
    existing.value = state.selected || "";
  }
  function render() {
    const creating = state.mode === "create", account = state.accounts.find(item => item.name === state.selected), locked = state.loading || state.saving;
    root.dataset.mode = state.mode;
    panel.setAttribute("aria-labelledby", creating ? createMode.id : editMode.id);
    root.setAttribute("aria-busy", String(locked));
    count.textContent = String(state.accounts.length);
    for (const [node, selected] of [[editMode, !creating], [createMode, creating]]) {
      node.setAttribute("aria-selected", String(selected)); node.disabled = state.saving;
      node.tabIndex = selected ? 0 : -1;
    }
    picker.hidden = creating;
    existing.disabled = locked || !state.accounts.length;
    existing.value = state.selected || "";
    reload.disabled = locked;
    empty.hidden = creating || !state.loaded || state.accounts.length > 0;
    emptyCreate.disabled = locked;
    chooseHint.hidden = creating || !!account || !state.loaded || !state.accounts.length;
    form.hidden = !creating && !account;
    formHeading.textContent = t(creating ? "createProfile" : "editProfile");
    selectedSummary.hidden = creating;
    selectedSummary.replaceChildren();
    if (account) selectedSummary.append(el("strong", account.name), el("span", t(account.role), "account-role-badge"));
    identity.hidden = !creating;
    name.required = creating;
    name.disabled = locked || !creating;
    role.disabled = locked;
    roleDescription.textContent = t(role.value === "dispatcher" ? "profileDispatcherPermissions" : "profileViewerPermissions");
    changeLabel.hidden = creating;
    changePassword.disabled = locked;
    const changingPassword = creating || changePassword.checked;
    retained.hidden = changingPassword;
    passwordLabel.hidden = !changingPassword;
    password.required = changingPassword;
    password.disabled = locked || !changingPassword;
    submit.disabled = locked;
    submit.textContent = t(state.saving ? "profileSaving" : creating ? "createProfile" : "saveProfileChanges");
    danger.hidden = creating || !account;
    remove.disabled = locked;
    remove.hidden = !!state.confirmation;
    confirmation.hidden = !state.confirmation;
    confirm.disabled = cancel.disabled = locked;
    if (state.confirmation) confirmText.replaceChildren(el("span", t("confirmDeleteAccount") + " "), el("strong", state.confirmation));
  }
  function select(value, reset = true) {
    state.selected = state.accounts.some(item => item.name === value) ? value : null;
    state.confirmation = null;
    if (reset) resetDraft();
    render();
  }
  function mode(value) {
    if (state.saving) return;
    state.mode = value;
    state.confirmation = null;
    if (value === "edit" && !state.accounts.some(item => item.name === state.selected)) state.selected = state.accounts[0]?.name || null;
    resetDraft(); render();
    if (value === "create") name.focus();
  }
  async function load({ preferred = state.selected, reset = false, autoSelect = false } = {}) {
    const version = ++state.version;
    state.loading = true;
    state.confirmation = null;
    listStatus.classList.remove("account-error");
    listStatus.textContent = t("loadingAccounts"); listStatus.hidden = false;
    render();
    try {
      const accounts = await ui.network.accounts();
      if (!current() || state.version !== version) return;
      if (!Array.isArray(accounts) || accounts.some(account => !validName(account?.name || "") || !["viewer", "dispatcher"].includes(account.role))) throw new Error("HOST_BAD_RESPONSE");
      state.accounts = accounts;
      state.loaded = true;
      options();
      if (state.mode === "edit") {
        const next = accounts.some(item => item.name === preferred) ? preferred : autoSelect ? accounts[0]?.name : null;
        select(next, reset || state.selected !== next);
      } else if (!accounts.some(item => item.name === state.selected)) state.selected = null;
      listStatus.textContent = ""; listStatus.hidden = true;
    } catch (error) {
      if (!current() || state.version !== version) return;
      listStatus.textContent = t(accountErrors.has(error.message) ? error.message : "accountsUnavailable");
      listStatus.classList.add("account-error");
    } finally {
      if (current() && state.version === version) { state.loading = false; render(); }
    }
  }
  editMode.onclick = () => mode("edit");
  createMode.onclick = emptyCreate.onclick = () => mode("create");
  modes.onkeydown = event => {
    if (!["ArrowLeft", "ArrowRight", "Home", "End"].includes(event.key) || state.saving) return;
    event.preventDefault();
    const next = event.key === "Home" ? "edit" : event.key === "End" ? "create" : state.mode === "edit" ? "create" : "edit";
    mode(next); (next === "edit" ? editMode : createMode).focus();
  };
  existing.onchange = () => { if (!state.loading && !state.saving) select(existing.value); };
  reload.onclick = () => { if (!state.loading && !state.saving) return load(); };
  role.onchange = () => render();
  changePassword.onchange = () => { password.value = ""; password.removeAttribute("aria-invalid"); report(); render(); };
  form.onsubmit = async event => {
    event.preventDefault();
    if (!current() || state.saving || state.loading) return;
    const creating = state.mode === "create", target = creating ? name.value : state.selected;
    if (!creating && !state.accounts.some(account => account.name === target)) return;
    name.removeAttribute("aria-invalid"); password.removeAttribute("aria-invalid");
    if (creating && !validName(target)) { name.setAttribute("aria-invalid", "true"); report("profileNameInvalid", true); name.focus(); return; }
    if (creating && state.accounts.some(account => account.name === target)) { name.setAttribute("aria-invalid", "true"); report("ACCOUNT_EXISTS", true); return; }
    const changedPassword = creating || changePassword.checked ? password.value : "";
    if ((creating || changePassword.checked) && !validPassword(changedPassword)) { password.setAttribute("aria-invalid", "true"); report("profilePasswordInvalid", true); password.focus(); return; }
    if (!["viewer", "dispatcher"].includes(role.value)) { report("INVALID_ACCOUNT", true); return; }
    const body = { role: role.value, password: changedPassword };
    state.saving = true; state.confirmation = null; report(); render();
    try {
      if (creating) await ui.network.createAccount({ name: target, ...body });
      else await ui.network.updateAccount(target, body);
      if (!current()) return;
      if (creating) state.accounts.push({ name: target, role: body.role });
      else state.accounts = state.accounts.map(account => account.name === target ? { name: target, role: body.role } : account);
      state.mode = "edit"; options(); select(target);
      await load({ preferred: target, reset: true });
      if (current()) { report(creating ? "profileCreated" : "accountSaved"); ui.toast(t(creating ? "profileCreated" : "accountSaved")); }
    } catch (error) {
      if (current()) report(accountErrors.has(error.message) ? error.message : "accountFailed", true);
    } finally {
      password.value = "";
      if (current()) { state.saving = false; render(); }
    }
  };
  remove.onclick = () => {
    if (state.loading || state.saving || state.mode !== "edit" || !state.selected) return;
    state.confirmation = state.selected; render(); confirm.focus();
  };
  cancel.onclick = () => { if (!state.saving) { state.confirmation = null; render(); remove.focus(); } };
  confirm.onclick = async () => {
    const target = state.confirmation;
    if (!current() || state.loading || state.saving || state.mode !== "edit" || !target || state.selected !== target || !state.accounts.some(account => account.name === target)) return;
    state.saving = true; render();
    try {
      await ui.network.deleteAccount(target);
      if (!current()) return;
      state.accounts = state.accounts.filter(account => account.name !== target); options(); select(null);
      await load({ preferred: null });
      if (current()) { listStatus.textContent = t("accountDeleted"); listStatus.hidden = false; ui.toast(t("accountDeleted")); }
    } catch (error) {
      if (current()) report(accountErrors.has(error.message) ? error.message : "accountFailed", true);
    } finally {
      if (current()) { state.saving = false; state.confirmation = null; render(); }
    }
  };
  options(); resetDraft(); render();
  void load({ autoSelect: true });
}
