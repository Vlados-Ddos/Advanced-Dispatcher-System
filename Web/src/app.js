import { readSetting } from "./storage.js";
import { applyPreferences } from "./preferences.js";
import { Store } from "./store.js";
import { Network } from "./network.js";
import { Renderer } from "./renderer.js";
import { UI } from "./ui.js";
import { PanelLayout } from "./panel-layout.js";
import { t, translate, language, setLanguage } from "./localization.js";

applyPreferences();
const store = new Store(),
  network = new Network(store),
  renderer = new Renderer(store),
  ui = new UI(store, network, renderer);
new PanelLayout(() => ui.renderRows());
for (const eventName of ["pointerdown", "keydown", "touchstart", "input", "wheel"])
  document.addEventListener(eventName, () => network.noteActivity(), { passive: true });
const $ = (id) => document.getElementById(id);
if (readSetting("ads.theme") === "light") document.body.classList.add("light");
store.addEventListener("change", () => {
  const lang = store.capabilities.language;
  if (lang && lang !== language()) setLanguage(lang);
});
translate();
renderer.theme();
function showLogin(message = "") {
  $("login-screen").hidden = false;
  for (const element of document.querySelectorAll(
    "body > header, body > main, #dashboard, #notice",
  ))
    element.inert = true;
  ui.setUser(null);
  $("login-error").textContent = message ? t(message) : "";
  const form=$("login-form");(form.dataset.owner==='true'?form.elements.password:form.elements.username).focus();
}
function signedIn() {
  $("login-screen").hidden = true;
  for (const element of document.querySelectorAll(
    "body > header, body > main, #dashboard, #notice",
  ))
    element.inert = false;
  $("login-error").textContent = "";
  ui.setUser(network.user);
}
$("login-form").onsubmit = async (e) => {
  e.preventDefault();
  const form = e.target;
  const button = form.querySelector("button");
  button.disabled = true;
  try {
    await network.login(form.dataset.owner === 'true' ? {bootstrap:form.elements.password.value} : {
      name: form.elements.username.value,
      password: form.elements.password.value,
    });
    form.elements.password.value = "";
    signedIn();
  } catch (error) {
    if (error.message === "AUTH_STALE") return;
    console.warn("ADS_LOGIN_FAILED", error.name);
    $("login-error").textContent = t(
      error.message === "AUTH_REQUIRED"
        ? form.dataset.owner === 'true' ? 'ownerCodeInvalid' : "loginFailed"
        : error.message === "RATE_LIMITED"
          ? "RATE_LIMITED"
        : error.message === "SESSION_ACTIVE"
            ? "SESSION_ACTIVE"
          : ["HOST_TIMEOUT", "HOST_UNAVAILABLE", "HOST_BAD_RESPONSE", "INSECURE_TRANSPORT"].includes(error.message)
            ? error.message
          : "SERVER_UNAVAILABLE",
    );
  } finally {
    form.elements.password.value = "";
    button.disabled = false;
  }
};
$("logout").onclick = async () => {
  const work = network.logout();
  const revision = network.authRevision;
  showLogin();
  try {
    await work;
  } catch (error) {
    if (revision === network.authRevision) $("login-error").textContent = t("LOGOUT_FAILED");
    console.warn("ADS_LOGOUT_FAILED", error.name);
  }
};
network.addEventListener("auth-expired", () => showLogin("AUTH_EXPIRED"));
$("connection-retry").onclick = async () => {
  if (await network.retryConnection()) signedIn();
};
// Retire old fragment-based links without ever using their IPC credential.
if(location.hash.includes('token='))history.replaceState(null,"",location.pathname+location.search);
$("owner-login-mode").onclick=()=> {
  const form=$("login-form"),owner=form.dataset.owner!=='true';form.dataset.owner=String(owner);
  form.elements.username.closest('label').hidden=owner;form.elements.username.required=!owner;
  form.elements.password.value='';form.elements.password.autocomplete=owner?'one-time-code':'current-password';
  form.elements.password.closest('label').querySelector('span').textContent=t(owner?'ownerCode':'password');
  form.elements.password.closest('label').querySelector('span').setAttribute('data-i18n',owner?'ownerCode':'password');
  $("owner-login-mode").textContent=t(owner?'accountLogin':'ownerLogin');
  $("owner-login-mode").setAttribute('data-i18n',owner?'accountLogin':'ownerLogin');
  form.elements.password.focus();
};
try {
  if (await network.me()) {
    signedIn();
    network.start();
  } else showLogin();
} catch (error) {
  console.warn("ADS_SESSION_RESTORE_FAILED", error.name);
  showLogin(["HOST_TIMEOUT", "HOST_UNAVAILABLE"].includes(error.message) ? error.message : "HOST_UNAVAILABLE");
}
setInterval(async () => {
  if (!network.user || document.hidden || network.exhausted || network.retryRequest) return;
  try {
    ui.health = await network.health();
  } catch {
    /* Health polling is auxiliary; websocket state remains authoritative. */
  }
}, 3000);
document.addEventListener("visibilitychange", () => {
  if (document.hidden) network.stop();
  else if (network.user) network.start();
});
// Read-only diagnostics for local profiling; commands still pass through the authenticated gateway.
window.adsDiagnostics = () => ({
  rendererMs: renderer.renderMs,
  frameWorkMs: renderer.frameWorkMs,
  maxFrameWorkMs: renderer.maxFrameWorkMs,
  placementMs: renderer.placementMs,
  fps: renderer.fps,
  cars: store.cars.size,
  tracks: store.tracks.size,
  signals: store.signals.size,
  connected: network.status,
  seq: store.seq.toString(),
});
window.dispatchEvent(new Event("ads-app-ready"));
