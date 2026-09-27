import { separateSignalBoxes } from "./signal-collisions.js";
import { packSignalGroups } from "./signal-groups.js";
import {
  makePlacementPlan,
  projectPlacementPlan,
} from "./signal-placement-plan.js";

// Pure copied geometry only. No game state, DOM, commands or timers live here.
globalThis.onmessage = ({ data }) => {
  try {
    const entries=packSignalGroups(data.entries);
    separateSignalBoxes(entries, data.rails, data.obstacles);
    const plan = makePlacementPlan(
      entries,
      data.reference,
      data.rails,
      data.obstacles,
    );
    plan.referencePositions = projectPlacementPlan(plan, data.reference);
    globalThis.postMessage({ id: data.id, plan });
  } catch (error) {
    globalThis.postMessage({ id: data.id, error: error.message });
  }
};
