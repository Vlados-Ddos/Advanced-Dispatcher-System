import { el } from "./dom.js";
import { t } from "./localization.js";
import { jobIcon } from "./job-icons.js";

export function isJobAction(event) {
  return event?.type === "job" && ["jobInProgress", "jobCompleted", "jobAbandoned"].includes(event.code);
}
export function jobEventContent(ui, event) {
  const root = el("div", undefined, "job-event");
  const job = ui.store.jobs.get(event.target);
  root.append(jobIcon(job), el("strong", t(event.code)));
  const player = el("button", event.actor || t("jobActorUnknown"));
  player.disabled = !event.actorId;
  player.onclick = () => {
    const item = ui.store.players.get(event.actorId);
    if (item) { ui.setTab("players"); ui.pick("players", item); }
    else ui.toast(t("eventPlayerUnavailable"));
  };
  const order = el("button", event.target || t("jobUnavailable"));
  order.disabled = !event.target;
  order.onclick = () => ui.openJob({ id: event.target });
  root.append(player, order);
  return root;
}
