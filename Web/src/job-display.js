import { jobIcon } from "./job-icons.js";
import {
  usableName,
  localizedValue,
  locationName,
  locationRecord,
} from "./display-names.js";
import { t, language } from "./localization.js";
import { el } from "./dom.js";

export const neutralJobColor = "#98a6b0";
export function jobColor(job) {
  // Colors are supplied by the actual booklet APIs, never inferred from enum
  // numbers, IDs or names of optional mods.
  return /^#[a-f\d]{6}$/i.test(job?.typeColor || "")
    ? job.typeColor
    : neutralJobColor;
}
export function jobTypeKey(job) {
  return typeof job?.type === "string" && job.type
    ? job.type
    : "unknownJobType";
}
export function jobTypeText(job) {
  if (
    usableName(job?.typeName) &&
    job.typeLanguage === language() &&
    !job.typeName.startsWith("passjobs/")
  )
    return job.typeName;
  if (!job?.type) return t("unknownJobType");
  return localizedValue(job.type, "unknownJobType");
}
export function relatedJobs(store, carIds) {
  const ids = new Set(carIds || []);
  const references = new Set(
    [...ids].map((id) => store.cars.get(id)?.job).filter(Boolean),
  );
  const rank = (j) =>
    j.state === "InProgress" || j.active ? 0 : j.state === "Available" ? 1 : 2;
  return [...store.jobs.values()]
    .filter((j) => references.has(j.id) || j.cars?.some((id) => ids.has(id)))
    .sort((a, b) => rank(a) - rank(b) || a.id.localeCompare(b.id));
}
const indexes = new WeakMap();
const palettes = new WeakMap();
function typePalette(store) {
  let cached = palettes.get(store);
  if (
    cached?.revision === store.jobRevision &&
    cached.source === store.jobs &&
    cached.size === store.jobs.size
  )
    return cached.colors;
  const colors = new Map();
  // Only real booklet colors; a missing/stale record must not win by arrival order.
  for (const job of [...store.jobs.values()]
    .filter((j) => validColor(j.typeColor))
    .sort(
      (a, b) =>
        String(a.typeSource || "").localeCompare(String(b.typeSource || "")) ||
        a.id.localeCompare(b.id),
    ))
    if (!colors.has(jobTypeKey(job)))
      colors.set(jobTypeKey(job), job.typeColor.toLowerCase());
  palettes.set(store, {
    revision: store.jobRevision,
    source: store.jobs,
    size: store.jobs.size,
    colors,
  });
  return colors;
}
export function primaryJob(store, car) {
  let index = indexes.get(store);
  if (
    !index ||
    index.revision !== store.jobRevision ||
    index.size !== store.jobs.size
  ) {
    index = {
      revision: store.jobRevision,
      size: store.jobs.size,
      cars: new Map(),
    };
    const jobs = [...store.jobs.values()]
      .filter(
        (j) =>
          j.dataQuality !== "stale" && (j.active || j.state === "Available"),
      )
      .sort(
        (a, b) =>
          Number(!a.active) - Number(!b.active) || a.id.localeCompare(b.id),
      );
    for (const job of jobs)
      for (const id of job.cars || [])
        if (!index.cars.has(id)) index.cars.set(id, job);
    indexes.set(store, index);
  }
  const linked = store.jobs.get(car.job);
  return (
    index.cars.get(car.id) ||
    (linked &&
    linked.dataQuality !== "stale" &&
    !linked.cars?.length &&
    (linked.active || linked.state === "Available")
      ? linked
      : null)
  );
}
export function colorBadge(job, color = jobColor(job)) {
  const badge = el("span", jobTypeText(job), "job-type-badge");
  const swatch = el("i");
  swatch.style.backgroundColor = color;
  badge.dataset.key = "job-color-" + jobTypeKey(job);
  badge.prepend(jobIcon(job), swatch);
  badge.title = job.typeSource || t("colorUnknown");
  return badge;
}
export function carJobAction(ui, root, ids) {
  const jobs = relatedJobs(ui.store, ids);
  let select;
  if (jobs.length > 1) {
    const label = el("label", t("relatedJobs"), "related-jobs");
    select = el("select");
    select.dataset.key = "related-job";
    select.dataset.preserveValue = "true";
    select.setAttribute("aria-label", t("relatedJobs"));
    for (const job of jobs) {
      const option = el(
        "option",
        `${job.id} · ${jobTypeText(job)} · ${t(job.state)}`,
      );
      option.value = job.id;
      select.append(option);
    }
    label.append(select);
    root.append(label);
  }
  const button = ui.button(
    root,
    "goToJob",
    () => {
      const id = select
        ? document.querySelector('#inspector [data-key="related-job"]')?.value
        : jobs[0]?.id;
      const job = ui.store.jobs.get(id);
      if (job) ui.openJob(job);
      else ui.toast(t("jobUnavailable"));
    },
    false,
    jobs.length > 0,
  );
  button.title = jobs.length ? t("goToJobHint") : t("jobUnavailable");
  if (!jobs.length) root.append(el("small", t("jobUnavailable")));
}
export function colorLegend(store, mode) {
  const root = el("div", undefined, "car-color-legend");
  root.dataset.key = "car-color-legend";
  root.append(el("p", t("colorHint_" + mode)));
  if (mode === "jobType") {
    const types = new Map();
    for (const job of store.jobs.values())
      if (!types.has(jobTypeKey(job))) types.set(jobTypeKey(job), job);
    for (const job of [...types.values()].sort((a, b) =>
      jobTypeKey(a).localeCompare(jobTypeKey(b)),
    ))
      root.append(
        colorBadge(
          job,
          typePalette(store).get(jobTypeKey(job)) || neutralJobColor,
        ),
      );
    root.append(
      colorBadge({ type: "noActiveJob" }),
      el("small", t("jobColorStatusHint")),
    );
  } else {
    const disclosure = el("details"),
      summary = el("summary", t("destinationLegend"));
    disclosure.dataset.key = "destination-colors";
    disclosure.append(summary);
    root.append(disclosure);
    const keys = new Map();
    for (const car of store.cars.values()) {
      if (car.locomotive || validColor(car.catalogColor)) continue;
      const key = carColorKey(store, car, mode);
      if (!keys.has(key)) keys.set(key, car);
    }
    const entries = [...keys].sort(([a], [b]) => a.localeCompare(b));
    for (const [key] of entries) {
      const label = key ? locationName(store, key) : t("destinationUnknown");
      const badge = el("span", label, "job-type-badge"),
        swatch = el("i");
      swatch.style.backgroundColor = destinationColor(store, key);
      badge.dataset.key = "destination-color-" + key;
      badge.prepend(swatch);
      disclosure.append(badge);
    }
  }
  return root;
}

export const colorModes = ["jobType", "destination"];
export const normalizeColorMode = (mode) =>
  colorModes.includes(mode) ? mode : "jobType";
const validColor = (value) => /^#[a-f\d]{6}$/i.test(value || "");
export function carColorKey(store, car, mode) {
  if (normalizeColorMode(mode) !== "destination") return "";
  const job = primaryJob(store, car);
  // A known ended/stale job cannot leave an obsolete destination on a car.
  if (!job && car.job && store.jobs.has(car.job)) return "";
  return job?.destination || (!car.job || job ? car.destination : "") || "";
}
export function destinationColor(store, id) {
  const location = locationRecord(store, id);
  if (validColor(location?.color)) return location.color.toLowerCase();
  const base = store.topology?.stations?.find((s) => s.id === id);
  return validColor(base?.color) ? base.color.toLowerCase() : neutralJobColor;
}
export function carDisplayColor(store, car, mode) {
  // The catalogue also contains handcar, tender, slug and caboose pages.
  if (validColor(car.catalogColor)) return car.catalogColor;
  if (car.locomotive) return neutralJobColor;
  if (normalizeColorMode(mode) === "jobType") {
    const job = primaryJob(store, car);
    return job
      ? typePalette(store).get(jobTypeKey(job)) || neutralJobColor
      : neutralJobColor;
  }
  return destinationColor(store, carColorKey(store, car, "destination"));
}
