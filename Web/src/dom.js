export const $ = (id) => document.getElementById(id);
export function el(tag, text, className) {
  const node = document.createElement(tag);
  if (text !== undefined) node.textContent = text;
  if (className) node.className = className;
  return node;
}

// Preserve live inspector controls, focus and pointer targets while replacing
// their data/handlers. Availability is applied in the same update as the data.
export function syncChildren(target, source) {
  const key = (node) =>
    node.nodeType === 1 ? node.dataset?.key || node.localName : "#text";
  let at = target.firstChild;
  for (const next of [...source.childNodes]) {
    if (
      next.nodeType === 1 &&
      next.dataset?.key &&
      at &&
      key(at) !== key(next)
    ) {
      let existing = at.nextSibling;
      while (existing && key(existing) !== key(next))
        existing = existing.nextSibling;
      if (existing) {
        target.insertBefore(existing, at);
        at = existing;
      }
    }
    if (!at || key(at) !== key(next)) {
      target.insertBefore(next, at);
      continue;
    }
    const current = at;
    at = at.nextSibling;
    if (current.nodeType === 3) {
      if (current.textContent !== next.textContent)
        current.textContent = next.textContent;
      continue;
    }
    const editing =
      next.dataset.authoritative !== "true" &&
      current === document.activeElement &&
      ["INPUT", "SELECT", "TEXTAREA"].includes(current.tagName);
    const savedValue = current.value;
    const preserveDisclosure =
      current.localName === "details" && next.dataset.controlledOpen !== "true";
    for (const attr of [...current.attributes])
      if (
        !(preserveDisclosure && attr.name === "open") &&
        !next.hasAttribute(attr.name) &&
        !(editing && attr.name === "value")
      )
        current.removeAttribute(attr.name);
    for (const attr of [...next.attributes])
      if (
        !(preserveDisclosure && attr.name === "open") &&
        !(editing && attr.name === "value") &&
        current.getAttribute(attr.name) !== attr.value
      )
        current.setAttribute(attr.name, attr.value);
    if (!editing) {
      syncChildren(current, next);
      if (["INPUT", "SELECT", "TEXTAREA"].includes(current.tagName)) {
        const keep =
          next.dataset.preserveValue === "true" &&
          (current.tagName !== "SELECT" ||
            [...current.options].some((option) => option.value === savedValue));
        current.value = keep ? savedValue : next.value;
      }
      if (current.tagName === "INPUT") current.checked = next.checked;
    } else if (current.tagName !== "SELECT") syncChildren(current, next);
    current.onclick = next.onclick;
    current.onchange = next.onchange;
    current.oninput = next.oninput;
    current.onkeydown = next.onkeydown;
    current.ontoggle = next.ontoggle;
  }
  while (at) {
    const next = at.nextSibling;
    at.remove();
    at = next;
  }
}
