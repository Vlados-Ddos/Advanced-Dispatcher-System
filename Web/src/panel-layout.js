import { readSetting, saveSetting } from "./storage.js";
import { t } from "./localization.js";
import { interfaceMetrics } from "./preferences.js";

const storageKey = "ads.panels";
const clamp = (n, min, max) => Math.min(max, Math.max(min, n));

export function normalizePanels(value) {
  return Object.fromEntries(
    ["navigation", "sidebar", "inspector"].map((name) => {
      const n = value?.[name]?.width;
      return [
        name,
        {
          width:
            typeof n === "number" && Number.isFinite(n)
              ? clamp(
                  n,
                  name === "navigation" ? 58.5 : 160,
                  name === "navigation" ? 260 : 1200,
                )
              : null,
        },
      ];
    }),
  );
}

// Stored dimensions use the 100% UI scale; viewport clamps never overwrite
// the preference, so moving to a smaller display does not lose the layout.
export function fitPanels(
  width,
  height,
  scale,
  desired,
  collapsed,
  inspectorOpen,
) {
  const compact = width / scale < 1280;
  const narrow = width / scale < 520;
  const navMax = Math.max(
    28,
    Math.min(260 * scale, width * (narrow ? 0.18 : 0.4)),
  );
  const navigationWidth = clamp(
    (desired.navigation?.width ?? 128) * scale,
    Math.min(58.5 * scale, navMax),
    navMax,
  );
  const available = Math.max(0, width - navigationWidth);
  const min = Math.min(260 * scale, available);
  const budget = compact ? available : Math.max(0, available - 360 * scale);
  const sizes = {};
  for (const [name, defaultWidth] of [
    ["sidebar", 360],
    ["inspector", 384],
  ]) {
    sizes[name] = {
      width: clamp((desired[name].width ?? defaultWidth) * scale, min, budget),
      height,
    };
  }
  if (!compact && !collapsed && inspectorOpen) {
    const extra = sizes.sidebar.width + sizes.inspector.width - budget;
    if (extra > 0) {
      const room = sizes.sidebar.width + sizes.inspector.width - 2 * min;
      for (const size of Object.values(sizes))
        size.width -= (extra * (size.width - min)) / Math.max(1, room);
    }
  }
  sizes.navigation = { width: navigationWidth, height };
  return { compact, sizes };
}

export class PanelLayout {
  constructor(onResize) {
    this.main = document.querySelector("main");
    this.panels = {
      navigation: document.getElementById("nav-frame"),
      sidebar: document.querySelector(".sidebar"),
      inspector: document.getElementById("inspector-frame"),
    };
    this.inspector = document.getElementById("inspector");
    try {
      this.desired = normalizePanels(JSON.parse(readSetting(storageKey)));
    } catch (error) {
      console.warn("ADS_PANEL_SETTINGS_RESET", error.name);
      this.desired = normalizePanels(null);
    }
    this.handles = [];
    for (const [name, panel] of Object.entries(this.panels)) {
      {
        const axis = "width";
        const handle = document.createElement("div");
        handle.className = "panel-resize resize-" + axis;
        handle.tabIndex = 0;
        handle.setAttribute("role", "separator");
        handle.setAttribute("aria-controls", panel.id);
        handle.setAttribute("aria-orientation", "vertical");
        handle.dataset.panel = name;
        handle.dataset.axis = axis;
        panel.append(handle);
        this.handles.push(handle);
        handle.addEventListener("pointerdown", (e) =>
          this.start(e, name, axis, handle),
        );
        handle.addEventListener("pointermove", (e) => this.move(e));
        handle.addEventListener("pointerup", (e) => this.finish(e));
        handle.addEventListener("pointercancel", (e) => this.finish(e, true));
        handle.addEventListener("lostpointercapture", (e) =>
          this.finish(e, true),
        );
        handle.addEventListener("dblclick", () => this.reset(name));
        handle.addEventListener("keydown", (e) => this.key(e, name, axis));
      }
    }
    this.labels();
    window.addEventListener("ads-language", () => this.labels());
    window.addEventListener("ads-display", () => this.apply());
    window.addEventListener("resize", () => this.apply());
    window.addEventListener("blur", () => this.finish(null, true));
    window.addEventListener("ads-reset-panels", () => this.reset());
    this.observer = new ResizeObserver(() => {
      this.apply();
      onResize();
    });
    this.observer.observe(this.main);
    this.observer.observe(this.panels.sidebar);
    this.mutations = new MutationObserver(() => {
      if (
        this.lastInspectorHidden !== this.inspector.hidden ||
        this.lastBodyClass !== document.body.className
      )
        this.apply();
    });
    this.mutations.observe(document.body, {
      attributes: true,
      attributeFilter: ["class"],
    });
    this.mutations.observe(this.inspector, {
      attributes: true,
      attributeFilter: ["hidden"],
    });
    this.apply();
  }
  labels() {
    for (const handle of this.handles) {
      const label =
        t(
          handle.dataset.panel === "navigation"
            ? "resizeNavigation"
            : handle.dataset.panel === "sidebar"
              ? "resizeList"
              : "resizeDetails",
        ) +
        ": " +
        t("resize" + handle.dataset.axis);
      handle.setAttribute("aria-label", label);
      handle.title = label + ". " + t("resizeHint");
    }
  }
  apply() {
    this.scale =
      parseFloat(getComputedStyle(document.documentElement).fontSize) /
      interfaceMetrics.baseFont;
    const { width, height } = this.main.getBoundingClientRect();
    const compact = width / this.scale < 1280;
    if (document.body.classList.contains("narrow") !== width / this.scale < 520)
      document.body.classList.toggle("narrow", width / this.scale < 520);
    if (
      compact &&
      !this.inspector.hidden &&
      !document.body.classList.contains("compact") &&
      document.getElementById("settings-panel")?.hidden !== false
    )
      document.body.classList.add("collapsed");
    if (compact !== document.body.classList.contains("compact"))
      document.body.classList.toggle("compact", compact);
    const fitted = fitPanels(
      width,
      height,
      this.scale,
      this.desired,
      document.body.classList.contains("collapsed"),
      !this.inspector.hidden,
    );
    this.sizes = fitted.sizes;
    const navWidth = this.sizes.navigation.width + "px";
    if (
      document.documentElement.style.getPropertyValue("--nav-width") !==
      navWidth
    )
      document.documentElement.style.setProperty("--nav-width", navWidth);
    for (const [name, size] of Object.entries(this.sizes)) {
      if (this.panels[name].style.width !== size.width + "px")
        this.panels[name].style.width = size.width + "px";
      this.panels[name].style.removeProperty("height");
    }
    for (const handle of this.handles) {
      const axis = handle.dataset.axis;
      handle.setAttribute("aria-valuemin", "0");
      handle.setAttribute("aria-valuemax", String(Math.round(width)));
      handle.setAttribute(
        "aria-valuenow",
        String(Math.round(this.sizes[handle.dataset.panel][axis])),
      );
    }
    this.lastInspectorHidden = this.inspector.hidden;
    this.lastBodyClass = document.body.className;
  }
  start(e, name, axis, handle) {
    if (e.button !== 0 || this.drag) return;
    e.preventDefault();
    this.drag = {
      name,
      axis,
      handle,
      id: e.pointerId,
      x: e.clientX,
      size: { ...this.sizes[name] },
      previous: { ...this.desired[name] },
    };
    handle.setPointerCapture(e.pointerId);
    document.body.classList.add("resizing-panels");
    handle.focus();
  }
  move(e) {
    const d = this.drag;
    if (!d || e.pointerId !== d.id) return;
    if ((e.buttons & 1) === 0) {
      this.finish(e);
      return;
    }
    this.pendingDragX = e.clientX;
    if (!this.moveFrame)
      this.moveFrame = requestAnimationFrame(() => {
        this.moveFrame = null;
        this.flushMove();
      });
  }
  flushMove() {
    const d = this.drag,
      x = this.pendingDragX;
    this.pendingDragX = null;
    if (!d || x == null) return;
    const sign = d.name === "inspector" ? -1 : 1;
    this.change(d.name, d.axis, d.size.width + (x - d.x) * sign);
  }
  change(name, axis, width) {
    const main = this.main.getBoundingClientRect();
    if (name === "navigation") {
      this.desired.navigation.width = clamp(width / this.scale, 58.5, 260);
      this.apply();
      return;
    }
    const compact = document.body.classList.contains("compact");
    const other = name === "sidebar" ? "inspector" : "sidebar";
    const otherVisible =
      other === "inspector"
        ? !this.inspector.hidden
        : !document.body.classList.contains("collapsed");
    const max =
      main.width -
      this.sizes.navigation.width -
      (compact
        ? 0
        : 360 * this.scale + (otherVisible ? this.sizes[other].width : 0));
    this.desired[name].width =
      clamp(width, Math.min(260 * this.scale, max), max) / this.scale;
    this.apply();
  }
  finish(e, cancel = false) {
    const d = this.drag;
    if (!d || (e && e.pointerId !== d.id)) return;
    if (!cancel) this.flushMove();
    else this.pendingDragX = null;
    if (this.moveFrame) globalThis.cancelAnimationFrame?.(this.moveFrame);
    this.moveFrame = null;
    this.drag = null;
    if (cancel) this.desired[d.name] = d.previous;
    if (d.handle.hasPointerCapture(d.id)) d.handle.releasePointerCapture(d.id);
    document.body.classList.remove("resizing-panels");
    this.apply();
    if (!cancel) this.save();
  }
  key(e, name, axis) {
    if (e.key === "Escape") {
      this.finish(null, true);
      return;
    }
    if (["Home", "Enter"].includes(e.key)) {
      e.preventDefault();
      this.reset(name);
      return;
    }
    if (!["ArrowLeft", "ArrowRight"].includes(e.key)) return;
    e.preventDefault();
    const step = e.shiftKey ? 40 : 10,
      size = this.sizes[name];
    const dx =
      (e.key === "ArrowRight" ? step : e.key === "ArrowLeft" ? -step : 0) *
      (name === "inspector" ? -1 : 1);
    this.change(name, axis, size.width + dx);
    this.save();
  }
  save() {
    try {
      saveSetting(storageKey, JSON.stringify(this.desired));
    } catch (error) {
      console.warn("ADS_PANEL_SETTINGS_NOT_SAVED", error.name);
    }
  }
  reset(name) {
    this.finish(null, true);
    if (name) this.desired[name] = { width: null };
    else this.desired = normalizePanels(null);
    this.apply();
    this.save();
  }
}
