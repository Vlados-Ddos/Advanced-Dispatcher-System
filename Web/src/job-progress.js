import { el } from "./dom.js";
import { t, number, language } from "./localization.js";
import {
  entityName,
  trackName,
  locationRecord,
  usableName,
  localizedValue,
} from "./display-names.js";
import { jobTrains } from "./operations.js";

export const passengerJob = (job) =>
  ["PassengerExpress", "PassengerLocal"].includes(job.type);
export const shuntingJob = (job) =>
  /^Shunting/i.test(job?.type || "") ||
  (job?.legs || []).some((leg) => /^Shunting/i.test(leg?.operation || ""));
export function taskProgress(leg) {
  return ["completed", "active", "pending", "blocked", "impossible"].includes(leg.progress)
    ? leg.progress
    : "unknown";
}
export function passengerStops(job) {
  const stops = [];
  let contiguous = false;
  for (const [index, leg] of (job.legs || []).entries()) {
    if (!leg.passengerStop) { contiguous = false; continue; }
    const place = leg.toTrack || leg.station || leg.id;
    let stop = stops.at(-1);
    if (!contiguous || !stop || stop.place !== place) {
      stop = {key:leg.id || String(index),place,track:leg.toTrack,station:leg.station,legs:[],indices:[]};
      stops.push(stop);
    }
    stop.legs.push(leg);stop.indices.push(index);contiguous=true;
  }
  const approaching = new Set();
  for (const [index, leg] of (job.legs || []).entries()) {
    if (leg.passengerStop || taskProgress(leg)!=="active") continue;
    const next=stops.find(stop=>stop.indices[0]>index && stop.track===leg.toTrack &&
      stop.legs.some(l=>taskProgress(l)!=="completed"));
    if(next)approaching.add(next.key);
  }
  return stops.map(stop=>({...stop,
    progress:stop.legs.every(l=>taskProgress(l)==="completed")?"completed":
      stop.legs.some(l=>taskProgress(l)==="active")||approaching.has(stop.key)?"active":
      stop.legs.some(l=>taskProgress(l)==="unknown")?"unknown":"pending",
  }));
}
export function passengerStopRecord(store, row) {
  return locationRecord(store, row.station) ||
    [...(store.locations?.values() || [])].find((s) => s.passenger && s.tracks?.includes(row.track));
}
export function passengerStopLabel(store, row) {
  const stop = passengerStopRecord(store, row),
    name = language() === "ru" ? row.nameRu || stop?.nameRu : row.nameEn || stop?.nameEn,
    label = usableName(name) ? name : usableName(row.name) ? row.name : usableName(stop?.name) ? stop.name : t("passengerStopName"),
    code = row.code || stop?.code;
  return label + (code ? ` [${code}]` : "");
}
export function requiredStops(job, from) {
  const result = [];
  for (const leg of job.legs || []) {
    if (!leg.passengerStop || taskProgress(leg) === "completed") continue;
    if (!leg.toTrack) return null;
    if (!result.length && leg.toTrack === from) continue;
    if (result.at(-1) !== leg.toTrack) result.push(leg.toTrack);
  }
  return result;
}
export function planningTasks(job) {
  const tasks = (job.legs || []).map((leg, index) => ({leg,index}));
  if(job.active)return tasks.filter(({leg})=>taskProgress(leg)==="active");
  if(job.state==="Available") {
    const first=tasks.find(({leg})=>taskProgress(leg)!=="completed");
    return first && taskProgress(first.leg)==="pending" ? [first] : [];
  }
  return [];
}
// UI mirror of the single-movement Host/native guards. This only decides
// whether to open a preview; the Host still validates the captured task,
// membership, occupancy and native route again before executing anything.
function shuntingRouteIssue(store, job, leg, trainId) {
  if (!shuntingJob(job)) return null;
  if (job.dataQuality !== "ready") return "JOB_TASK_UNAVAILABLE";
  if (leg.type !== "Transport" || leg.operation && leg.operation !== "None") return "JOB_SHUNTING_PLANNER_UNSUPPORTED";
  if (!leg.toTrack || !store.tracks.has(leg.toTrack)) return "JOB_STOP_TRACK_UNAVAILABLE";
  if (leg.couplingRequired) return "JOB_WAGON_EXTRACTION_REQUIRED";
  const ids = leg.cars || [], required = new Set(ids);
  if (!ids.length || required.size !== ids.length) return "JOB_TASK_UNAVAILABLE";
  const usable = car => car && (!car.availability || car.availability === "available") && !car.derailed && car.nativeTrainset === true && car.couplersKnown === true && car.track1 && car.track2;
  const taskCars = ids.map(id => store.cars.get(id));
  if (taskCars.some(car => !usable(car) || !car.consist)) return "JOB_TASK_UNAVAILABLE";
  const consist = taskCars[0].consist;
  if (taskCars.some(car => car.consist !== consist)) return "JOB_WAGON_EXTRACTION_REQUIRED";
  const selectedConsist = store.cars.get(trainId)?.consist || trainId;
  if (selectedConsist && selectedConsist !== consist || !store.train(selectedConsist || consist)) return "JOB_ROUTE_START_REQUIRED";
  const members = [...store.cars.values()].filter(car => car.consist === consist);
  if (members.some(car => !usable(car))) return "JOB_TASK_UNAVAILABLE";
  if (members.some(car => !required.has(car.id) && !car.locomotive && !["tender", "slug"].includes(car.vehicleCategory))) return "JOB_WAGON_EXTRACTION_REQUIRED";
  if (!members.some(car => car.locomotive)) return "JOB_ROUTE_START_REQUIRED";
  const seen = new Set(), pending = [ids[0]];
  while (pending.length) {
    const id = pending.pop();
    if (seen.has(id)) continue;
    seen.add(id);
    const car = store.cars.get(id);
    for (const neighborId of [car.coupledFront, car.coupledRear].filter(Boolean)) {
      const neighbor = store.cars.get(neighborId);
      if (!usable(neighbor) || neighbor.consist !== consist || neighborId === id ||
          ![neighbor.coupledFront, neighbor.coupledRear].includes(id)) return "JOB_TASK_UNAVAILABLE";
      if (!seen.has(neighborId)) pending.push(neighborId);
    }
  }
  if (seen.size !== members.length) return "JOB_WAGON_EXTRACTION_REQUIRED";
  return null;
}
export function beginOrderRoute(ui, jobId, index, trainId, expectedTaskId) {
  const job = ui.store.jobs.get(jobId),
    leg = job?.legs?.[index];
  if (
    !job ||
    job.dataQuality === "stale" ||
    !leg?.id ||
    expectedTaskId != null && leg.id !== expectedTaskId ||
    !planningTasks(job).some((task)=>task.index===index)
  )
    return ui.toast(t("JOB_TASK_CHANGED"), true);
  const existing = ui.store.routes.find(
    (r) =>
      !r.endedAt &&
      r.lifecycle !== "failed" &&
      r.jobId === jobId &&
      (passengerJob(job) || r.taskId === leg.id),
  );
  if (existing) return ui.pick("routes", existing);
  const issue = shuntingRouteIssue(ui.store, job, leg, trainId);
  if (issue) return ui.toast(t(issue), true);
  if (shuntingJob(job)) trainId = ui.store.cars.get(trainId)?.consist || trainId;
  const members = jobTrains({cars: leg.cars?.length ? leg.cars : job.cars || []}, ui.store);
  trainId ||= members.length === 1 ? members[0] : null;
  const train = ui.store.train(trainId),
    taskCars = (leg.cars?.length ? leg.cars : job.cars || []).map(id=>ui.store.cars.get(id)).filter(Boolean),
    carTracks = [...new Set(taskCars.flatMap(car=>[car.track1,car.track2]).filter(Boolean))],
    from = train?.track1 || leg.fromTrack || (carTracks.length === 1 ? carTracks[0] : null);
  if (!from) return ui.toast(t("JOB_ROUTE_START_REQUIRED"), true);
  const stops = passengerJob(job) ? requiredStops(job, from) : [leg.toTrack];
  if (!stops?.length || stops.some((id) => !id || !ui.store.tracks.has(id)))
    return ui.toast(t("JOB_STOP_TRACK_UNAVAILABLE"), true);
  ui.beginRoute(from, trainId || null);
  ui.routeJobId = jobId;
  ui.routeTaskIndex = index;
  ui.routeTaskId = leg.id;
  ui.endTrack = stops.at(-1);
  ui.routeVia = stops.slice(0, -1);
  ui.requiredRouteVia = [...ui.routeVia];
  ui.previewRoute();
  ui.renderInspector();
}
export function renderJobProgress(ui, root, job, includeActions = true) {
  const legs = job.legs || [];
  if (!legs.length) return;
  const section = el("section", undefined, "job-progress");
  section.dataset.key = "job-progress";
  const list = el("ol");
  list.dataset.key = "job-progress-list";
  const rows = passengerJob(job)
    ? passengerStops(job)
    : legs.map((leg, index) => ({
        key: leg.id || String(index),
        track: leg.toTrack || leg.fromTrack,
        station: leg.station,
        progress: taskProgress(leg),
        legs: [leg],
        indices: [index],
      }));
  section.append(
    el(
      "h3",
      rows.length===0 && passengerJob(job) ? t("passengerStopsUnavailable") : t(passengerJob(job) ? "stopProgress" : "progress") +
        " · " +
        number(rows.filter((r) => r.progress === "completed").length) +
        " / " +
        number(rows.length),
    ),
  );
  for (const row of rows) {
    const item = el(
      "li",
      undefined,
      "job-progress-item job-progress-" + row.progress,
    );
    item.dataset.key = "task-" + row.key;
    const marker = el(
      "span",
      { completed: "✓", active: "●", pending: "○", blocked: "⚠", impossible: "✕", unknown: "?" }[row.progress],
      "job-progress-marker",
    );
    marker.setAttribute("aria-hidden", "true");
    const body = el("div", undefined, "job-progress-body");
    const label = passengerJob(job)
      ? passengerStopLabel(ui.store, row)
      : localizedValue(
          row.legs[0].operation && row.legs[0].operation !== "None"
            ? row.legs[0].operation
            : row.legs[0].type,
          "taskTypeUnavailable",
        );
    const focus = el("button", label, "job-progress-target");
    focus.onclick = () => {
      const location = passengerJob(job) ? passengerStopRecord(ui.store, row) : ui.location(row.station),
        track = ui.store.tracks.get(row.track);
      // Passenger stops are named locations. Selecting their internal rail
      // made the progress panel expose implementation names such as
      // doubletrack Main-9-T and sent the camera to the wrong object.
      if (location) ui.pickNested("locations", location);
      else if (track) ui.pick("tracks", track);
    };
    focus.disabled =
      !ui.store.tracks.has(row.track) && !passengerStopRecord(ui.store, row);
    body.append(focus, el("small", t("task_" + row.progress)));
    if (row.progress === "active") item.setAttribute("aria-current", "step");
    if (!passengerJob(job)) {
      const leg = row.legs[0];
      body.append(
        el(
          "p",
          [
            leg.fromTrack ? trackName(ui.store, leg.fromTrack) : leg.from,
            leg.toTrack ? trackName(ui.store, leg.toTrack) : leg.to,
          ]
            .filter(Boolean)
            .join(" → "),
        ),
      );
      const wagonIds = [...new Set(leg.cars || [])];
      if (wagonIds.length) {
        const wagons = el("details", undefined, "task-wagons");
        wagons.dataset.key = "task-wagons-" + row.key;
        const summary = el("summary", `${t("jobWagons")} (${number(wagonIds.length)})`);
        wagons.append(summary);
        const list = el("div", undefined, "task-wagon-list");
        for (const id of wagonIds) {
          const car = ui.store.cars.get(id),
            button = el("button", car ? entityName(ui.store, "cars", car) : t("jobWagonUnavailable"), "task-wagon");
          button.disabled = !car;
          button.onclick = () => { const current = ui.store.cars.get(id); if (current) ui.pick("cars", current); };
          list.append(button);
        }
        wagons.append(list);
        body.append(wagons);
      }
      const members = new Set(leg.cars || []);
      const trapped = [...members].map(id=>ui.store.cars.get(id)).filter(car=>
        car?.couplersKnown && car.coupledFront && car.coupledRear &&
        !members.has(car.coupledFront) && !members.has(car.coupledRear));
      if (trapped.length) body.append(el("p", t("JOB_WAGON_EXTRACTION_REQUIRED"), "warning"));
      if (leg.couplingRequired) body.append(el("small", t("couplingRequired")));
      if (leg.handbrakeRequired)
        body.append(el("small", t("handbrakeRequired")));
      if (leg.cargoAmount > 0)
        body.append(el("small", t("cargo") + ": " + number(leg.cargoAmount)));
    }
    item.append(marker, body);
    list.append(item);
  }
  section.append(list);
  if (!includeActions) {
    root.append(list);
    return;
  }
  const active = planningTasks(job);
  const actions = el("div", undefined, "job-progress-actions");
  if ((job.active || job.state === "Available") && job.dataQuality !== "stale" && active.length) {
    const trains = jobTrains({cars: active.flatMap(({leg}) => leg.cars?.length ? leg.cars : job.cars || [])}, ui.store);
    const select = el("select");
    select.setAttribute("aria-label", t("routeFromTrain"));
    select.dataset.key = "task-route-train";
    select.dataset.preserveValue = "true";
    const empty = el("option", t("JOB_ROUTE_START_REQUIRED"));
    empty.value = "";
    select.append(empty);
    for (const id of trains) {
      const option = el(
        "option",
        entityName(ui.store, "trains", ui.store.train(id)),
      );
      option.value = id;
      select.append(option);
    }
    select.value = trains.includes(ui.startTrain)
      ? ui.startTrain
      : trains.length === 1
        ? trains[0]
        : "";
    if (trains.length > 1) section.append(select);
    for (const { leg, index } of passengerJob(job)
      ? active.slice(0, 1)
      : active) {
      const issue = shuntingRouteIssue(ui.store, job, leg);
      if (issue) {
        section.append(el("p", t(issue), "warning"));
        continue;
      }
      if (!leg.toTrack && !passengerJob(job)) {
        section.append(el("p", t("JOB_STOP_TRACK_UNAVAILABLE")));
        continue;
      }
      const taskId = leg.id;
      const b = ui.button(
        actions,
        "planOrderRoute",
        () => {
          const train =
            trains.length > 1
              ? document.querySelector(
                  '#inspector [data-key="task-route-train"]',
                )?.value
              : trains[0];
          if (trains.length > 1 && !train)
            return ui.toast(t("JOB_ROUTE_START_REQUIRED"), true);
          beginOrderRoute(ui, job.id, index, train, taskId);
        },
        false,
      );
      b.dataset.key = "task-route-" + leg.id;
      if (active.length > 1 && !passengerJob(job))
        b.textContent += " · " + number(index + 1);
    }
  }
  root.append(section);
  if (actions.children.length) root.append(actions);
}
