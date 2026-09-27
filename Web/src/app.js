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
const $ = (id) => document.getElementById(id);
if (readSetting("ads.theme") === "light") document.body.classList.add("light");
store.addEventListener("change", () => {
  const lang = store.capabilities.language;
  if (lang && lang !== language()) setLanguage(lang);
});
translate();
renderer.theme();
function showLogin() {
  $("login-screen").hidden = false;
  for (const element of document.querySelectorAll(
    "body > header, body > main, #dashboard, #notice",
  ))
    element.inert = true;
  ui.setUser(null);
  $("login-form").elements.username.focus();
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
    await network.login({
      name: form.elements.username.value,
      password: form.elements.password.value,
    });
    form.elements.password.value = "";
    signedIn();
  } catch (error) {
    console.warn("ADS_LOGIN_FAILED", error.name);
    $("login-error").textContent = t(
      error.message === "AUTH_REQUIRED"
        ? "loginFailed"
        : error.message === "RATE_LIMITED"
          ? "RATE_LIMITED"
          : "SERVER_UNAVAILABLE",
    );
  } finally {
    button.disabled = false;
  }
};
$("logout").onclick = async () => {
  try {
    await network.logout();
    showLogin();
  } catch (error) {
    showLogin();
    $("login-error").textContent = t("LOGOUT_FAILED");
    console.warn("ADS_LOGOUT_FAILED", error.name);
  }
};
network.addEventListener("auth-expired", showLogin);
const bootstrap = new URLSearchParams(location.hash.slice(1)).get("token");
if (bootstrap)
  history.replaceState(null, "", location.pathname + location.search);
try {
  if (bootstrap) {
    await network.login({ bootstrap });
    signedIn();
  } else if (await network.me()) {
    signedIn();
    network.start();
  } else showLogin();
} catch (error) {
  console.warn("ADS_SESSION_RESTORE_FAILED", error.name);
  showLogin();
}
setInterval(async () => {
  if (!network.user || document.hidden) return;
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
