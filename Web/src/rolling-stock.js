// Drawing, picking and selection use the same set at overview zoom. Individual
import { lodFactor } from "./lod.js";
// cars retain their real poses at detail zoom, including curved consists.
export function* mapCars(renderer) {
  if (renderer.scale >= 0.24 * lodFactor("lodRollingStock")) {
    yield* renderer.store.cars.values();
    return;
  }
  const representatives = new Map();
  const group =
    renderer.selected?.kind === "wagonGroups"
      ? renderer.store.wagonGroup(renderer.selected.id)
      : null;
  const groupIds = new Set(group?.carIds || []);
  const priority = (car) =>
    renderer.selected?.kind === "cars" && renderer.selected.id === car.id
      ? 3
      : car.locomotive
        ? 2
        : car.catalogColor
          ? 1
          : 0;
  for (const car of renderer.store.cars.values()) {
    const current = representatives.get(car.consist);
    if (
      !current ||
      priority(car) > priority(current) ||
      (priority(car) === priority(current) &&
        (car.order ?? 0) < (current.order ?? 0))
    )
      representatives.set(car.consist, car);
  }
  for (const id of groupIds) {
    const car = renderer.store.cars.get(id);
    if (car) yield car;
  }
  for (const car of representatives.values())
    if (!groupIds.has(car.id)) yield car;
}

export function carPath(ctx, shape, locomotive, padding = 0) {
  const length = shape.width + padding * 2,
    width = shape.height + padding * 2,
    nose = locomotive ? Math.min(5, length / 3) : 0;
  ctx.beginPath();
  ctx.moveTo(-length / 2, -width / 2);
  ctx.lineTo(length / 2 - nose, -width / 2);
  ctx.lineTo(length / 2, 0);
  ctx.lineTo(length / 2 - nose, width / 2);
  ctx.lineTo(-length / 2, width / 2);
  ctx.closePath();
}

// Native categories are produced by DV CarTypes, never inferred from IDs.
export function isWagon(car) {
  if (car?.locomotive) return false;
  return ["wagon", "caboose"].includes(car?.vehicleCategory);
}
export function isLocomotive(car) {
  return (
    car?.locomotive === true &&
    car.kind !== "trains" &&
    car.kind !== "wagonGroups" &&
    (!car.vehicleCategory || car.vehicleCategory === "locomotive")
  );
}
export function trainDescription(train) {
  if (train.locomotiveCount > 1) return "multipleLocomotives";
  if (train.locomotiveCount === 1 || train.locomotive)
    return train.count === 1 ? "singleLocomotive" : "trainConsist";
  return train.count === 1
    ? "singleVehicle"
    : train.wagonCount === train.count
      ? "wagonGroup"
      : "rollingStockGroup";
}

export function vehicleHeading(car) {
  return car?.locomotive
    ? "locomotiveUnit"
    : { wagon: "cars", caboose: "caboose", tender: "tender", slug: "slug" }[
        car?.vehicleCategory
      ] || "rollingStock";
}
