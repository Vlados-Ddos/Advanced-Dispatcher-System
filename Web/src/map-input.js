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
  const touches = new Map();
  let pinch = null;
  const pinchPose = () => {
    const [a,b]=[...touches.values()];
    return a&&b?{x:(a.x+b.x)/2,y:(a.y+b.y)/2,distance:Math.hypot(a.x-b.x,a.y-b.y)}:null;
  };
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
    if(e.pointerType==="touch" && !control(e.target)) {
      touches.set(e.pointerId,{x:e.clientX,y:e.clientY});
      if(touches.size===2) {
        e.preventDefault();drag=null;renderer.panning=false;root.classList.remove("dragging");pinch=pinchPose();root.setPointerCapture(e.pointerId);return;
      }
    }
    if (e.button !== 0 || e.isPrimary === false || control(e.target) || drag)
      return;
    e.preventDefault();
    root.focus({ preventScroll: true });
    root.setPointerCapture(e.pointerId);
    drag = { id: e.pointerId, x: e.clientX, y: e.clientY, moved: false };
    renderer.panning = true;
    renderer.tooltip?.hide();
    root.classList.add("dragging");
  });
  on(root, "pointermove", (e) => {
    if(touches.has(e.pointerId))touches.set(e.pointerId,{x:e.clientX,y:e.clientY});
    if(pinch && touches.size>=2) {
      e.preventDefault();const next=pinchPose(),rect=root.getBoundingClientRect();
      if(pinch.distance>0 && next.distance>0)renderer.zoom(next.distance/pinch.distance,next.x-rect.left,next.y-rect.top);
      if(!renderer.follow) {renderer.cx-=(next.x-pinch.x)/renderer.scale;renderer.cz+=(next.y-pinch.y)/renderer.scale;renderer.invalidate();}
      pinch=next;return;
    }
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
    renderer.follow = null;
    renderer.cameraFocus = null;
    // Incremental displacement uses the current scale, including wheel zoom
    // during a captured gesture. There is no stale starting camera/scale.
    renderer.cx -= dx / renderer.scale;
    renderer.cz += dy / renderer.scale;
    drag.x = e.clientX;
    drag.y = e.clientY;
    renderer.invalidate();
  });
  on(root, "pointerup", (e) => {
    touches.delete(e.pointerId);
    if(pinch) {
      if(root.hasPointerCapture(e.pointerId))root.releasePointerCapture(e.pointerId);
      pinch=touches.size>=2?pinchPose():null;finish();
      if(touches.size===1) {
        const [id,p]=[...touches][0];drag={id,x:p.x,y:p.y,moved:true};
        renderer.panning=true;root.classList.add("dragging");
      }
      return;
    }
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
    ) {
      // Alt is a details modifier for switches.  It must use the normal
      // object hit-test; forcing tracksOnly here made Alt+click select the
      // rail underneath the turnout.  The callback returns true when the
      // click-to-switch setting consumed the gesture.  In that case the
      // existing selection/panel is deliberately left untouched.
      const picked = renderer.hit(e.clientX - r.left, e.clientY - r.top);
      const consumed = picked?.kind === "switches" && !e.altKey
        ? renderer.onSwitchClick?.(picked) === true
        : false;
      if (!consumed) renderer.select(picked);
    }
  });
  for (const event of ["lostpointercapture", "pointercancel"])
    on(root, event, (e) => {
      touches.delete(e.pointerId);
      if(touches.size<2)pinch=null;
      if (drag?.id === e.pointerId) finish();
    });
  on(root, "pointerleave", () => {
    renderer.pointer = null;
    renderer.pointerClient = null;
    renderer.tooltip?.hide();
    root.classList.remove("interactive");
  });
  on(window, "blur", () => {
    touches.clear();pinch=null;
    finish();
    renderer.pointer = renderer.pointerClient = null;
    renderer.tooltip?.hide();
  });
  on(document, "visibilitychange", () => {
    if (document.hidden) {
      touches.clear();pinch=null;
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
    for(const id of touches.keys())if(root.hasPointerCapture(id))root.releasePointerCapture(id);
    touches.clear();pinch=null;
    finish();
    for (const off of bindings.splice(0)) off();
    renderer.pointer = null;
    renderer.tooltip?.hide();
    root.classList.remove("interactive");
  };
}
