import { t, compareNames } from "./localization.js";
import { entityName } from "./display-names.js";

export function jobOwner(job, store) {
  if(job.assignedPlayerKey) {
    const player=[...store.players.values()].find(p=>p.identityKey===job.assignedPlayerKey);
    return {key:job.assignedPlayerKey==="local"?"local":"player:"+job.assignedPlayerKey,
      name:player?entityName(store,"players",player):job.assignedPlayerName||t("ownerUnavailable")};
  }
  const state = ["Available", "Expired"].includes(job.state)
    ? "unassigned"
    : job.ownerStatus;
  if (state === "local")
    return {
      key: "local",
      name: entityName(
        store,
        "players",
        store.players.get("local") || { id: "local", name: "Player" },
      ),
    };
  if (state === "assigned" || job.owner)
    return {
      key: "player:" + (job.ownerKey || job.owner || "unknown"),
      name: job.owner || t("ownerUnavailable"),
    };
  const labels = {
    shared: "ownerShared",
    unassigned: "ownerUnassigned",
    unknown: "ownerUnavailable",
  };
  return {
    key: labels[state] || "ownerUnavailable",
    name: t(labels[state] || "ownerUnavailable"),
  };
}
export function groupedJobs(jobs, store, collapsed = new Set()) {
  const groups = new Map();
  for (const job of jobs) {
    const owner = jobOwner(job, store);
    if (!groups.has(owner.key)) groups.set(owner.key, { ...owner, jobs: [] });
    groups.get(owner.key).jobs.push(job);
  }
  const rows = [];
  for (const g of [...groups.values()].sort(
    (a, b) => compareNames(a.name, b.name) || a.key.localeCompare(b.key),
  )) {
    rows.push({
      group: true,
      id: "owner:" + g.key,
      key: g.key,
      name: g.name,
      count: g.jobs.length,
    });
    if (!collapsed.has(g.key)) rows.push(...g.jobs);
  }
  return rows;
}
