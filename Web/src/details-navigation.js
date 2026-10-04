import { el } from "./dom.js";
import { t } from "./localization.js";

// All detail panels use the same small, content-width navigation control. The
// callback is kept in the caller so the shared stack remains authoritative.
export function detailsBack(ui, label = "back", fallback = null) {
  const button = el("button", "← " + t(label), "details-back");
  button.type = "button";
  button.dataset.key = "details-back";
  button.onclick = () => {
    if (!ui.backDetails() && fallback) fallback();
  };
  return button;
}
