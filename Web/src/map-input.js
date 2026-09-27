// Own a single pointer gesture. Browser drag-and-drop and text selection are
// different input systems and must not take over a canvas pan.
export function bindMapInput(renderer) {
  const root = renderer.root;
  const bindings = [];
  const on = (target, event, handler, options) => {
    target.addEventListener(event, handler, options);
    bindings.push(() => target.removeEventListener?.(event, handler, options));
  };
  let drag = null;
  const control = (target) =>
    target?.closest?.(
      "button,input,select,textarea,a,[contenteditable=true],[data-map-control]",
    );
  const finish = () => {
    const id = drag?.id;
    drag = null;
    renderer.panning = false;
    renderer.hoverDirty = true;
    root.classList.remove("dragging");
    if (id != null && root.hasPointerCapture(id))
      root.releasePointerCapture(id);
  };
  renderer.cancelPan = finish;
  on(root, "dragstart", (e) => e.preventDefault());
  on(root, "pointerdown", (e) => {
    if (e.button !== 0 || e.isPrimary === false || control(e.target) || drag)
      return;
    e.preventDefault();
    root.focus({ preventScroll: true });
    root.setPointerCapture(e.pointerId);
    drag = { id: e.pointerId, x: e.clientX, y: e.clientY, moved: false };
    renderer.panning = true;
    renderer.tooltip?.hide();
    renderer.follow = null;
    renderer.cameraFocus = null;
    root.classList.add("dragging");
  });
  on(root, "pointermove", (e) => {
    const r = root.getBoundingClientRect();
    renderer.pointerClient = [e.clientX, e.clientY];
    renderer.pointer = [e.clientX - r.left, e.clientY - r.top];
    renderer.hoverDirty = true;
    if (!drag || e.pointerId !== drag.id) return;
    if ((e.buttons & 1) === 0) {
      finish();
      return;
    }
    const dx = e.clientX - drag.x,
      dy = e.clientY - drag.y;
    if (!drag.moved && Math.abs(dx) + Math.abs(dy) <= 4) return;
    drag.moved = true;
    // Incremental displacement uses the current scale, including wheel zoom
    // during a captured gesture. There is no stale starting camera/scale.
    renderer.cx -= dx / renderer.scale;
    renderer.cz += dy / renderer.scale;
    drag.x = e.clientX;
    drag.y = e.clientY;
    renderer.invalidate();
  });
  on(root, "pointerup", (e) => {
    if (!drag || e.pointerId !== drag.id || e.button !== 0) return;
    const click = !drag.moved;
    finish();
    const r = root.getBoundingClientRect();
    if (
      click &&
      e.clientX >= r.left &&
      e.clientX <= r.right &&
      e.clientY >= r.top &&
      e.clientY <= r.bottom
    )
      renderer.select(
        renderer.hit(e.clientX - r.left, e.clientY - r.top, {
          tracksOnly: e.altKey,
        }),
      );
  });
  for (const event of ["lostpointercapture", "pointercancel"])
    on(root, event, (e) => {
      if (drag?.id === e.pointerId) finish();
    });
  on(root, "pointerleave", () => {
    renderer.pointer = null;
    renderer.pointerClient = null;
    renderer.tooltip?.hide();
    root.classList.remove("interactive");
  });
  on(window, "blur", () => {
    finish();
    renderer.pointer = renderer.pointerClient = null;
    renderer.tooltip?.hide();
  });
  on(document, "visibilitychange", () => {
    if (document.hidden) {
      finish();
      renderer.pointer = renderer.pointerClient = null;
      renderer.tooltip?.hide();
    }
  });
  on(
    root,
    "wheel",
    (e) => {
      if (control(e.target)) return;
      e.preventDefault();
      renderer.resize?.();
      const r = root.getBoundingClientRect();
      const delta =
        e.deltaY *
        (e.deltaMode === 1 ? 16 : e.deltaMode === 2 ? root.clientHeight : 1);
      renderer.zoom(
        Math.exp(-delta * 0.0015),
        e.clientX - r.left,
        e.clientY - r.top,
      );
    },
    { passive: false },
  );
  on(root, "keydown", (e) => {
    if (control(e.target)) return;
    if (e.key === "+" || e.key === "=") renderer.zoom(1.3);
    else if (e.key === "-") renderer.zoom(1 / 1.3);
    else if (e.key === "Home") {
      finish();
      renderer.fit();
    } else if (e.key === "Escape") {
      finish();
      renderer.select(null);
    } else return;
    e.preventDefault();
  });
  return () => {
    finish();
    for (const off of bindings.splice(0)) off();
    renderer.pointer = null;
    renderer.tooltip?.hide();
    root.classList.remove("interactive");
  };
}
