const caches = new WeakMap();
export function isSignalBoard(part) {
  return ["fixedBoard", "distantBoard", "distantShort", "shuntLimit"].includes(
    part?.kind,
  );
}
export function isSignal(object) {
  return (
    object?.objectKind !== "sign" && object?.displayLayer !== "additionalSigns"
  );
}
// Derived views retain the native identity; no duplicate copy participates in commands.
export function railwaySigns(store) {
  const old = caches.get(store);
  if (
    old &&
    old.revision === store.presentationRevision &&
    old.signs === store.signs &&
    old.signals === store.signals &&
    old.signCount === store.signs?.size &&
    old.signalCount === store.signals?.size
  )
    return old.items;
  const items = [...(store.signs?.values() || [])];
  for (const s of store.signals?.values() || []) {
    if (!isSignal(s))
      items.push({
        ...s,
        signalObject: true,
        signKind: s.signKind || s.visualKind || "fixedBoard",
        source: "DV Signals",
        speeds: [],
        types: [s.signKind || "fixedBoard"],
      });
    for (const part of s.parts || [])
      if (isSignalBoard(part))
        items.push({
          id: s.id + ":" + part.id,
          parentSignal: s.id,
          visible: part.visible,
          signalObject: true,
          source: "DV Signals",
          signKind: part.kind,
          text: part.text,
          color: part.color,
          x: part.worldX,
          z: part.worldZ,
          track: s.track,
          span: s.span,
          direction: s.direction,
          speeds: [],
          types: [part.kind],
          parts: [],
        });
  }
  caches.set(store, {
    revision: store.presentationRevision,
    signs: store.signs,
    signals: store.signals,
    signCount: store.signs?.size,
    signalCount: store.signals?.size,
    items,
  });
  return items;
}

export function signVisible(sign, layers) {
  return (
    sign.visible !== false &&
    (sign.signalObject ? !!layers.additionalSigns : !!layers.signs)
  );
}
