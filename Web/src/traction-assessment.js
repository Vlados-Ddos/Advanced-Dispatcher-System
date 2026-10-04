import { locomotiveLoadRating } from "./locomotive-catalog.js";

// The game publishes generated traction and live mass; catalogue ratings are
// the only stable load envelope available when no route grade is exposed by
// the native capture contract.  Use the conservative 2% rating, selecting the
// wet value when current surface wetness is known.  This keeps the result tied
// to real game data and clearly reports unknown conditions instead of guessing.
export function tractionAssessment(item, weather = {}) {
  const rating = item?.tractionRating || locomotiveLoadRating(item?.catalogModel || item?.model || item?.name);
  const mass = item?.consistMass ?? item?.mass;
  const wetness = weather?.wetness;
  if (!rating || item?.massKnown !== true || !Number.isFinite(mass) || mass <= 0 ||
      weather?.wetnessKnown !== true || !Number.isFinite(wetness) || wetness < 0 || wetness > 1)
    return { key: "tractionUnknown", limit: null, wet: null };
  const wet = wetness > 0.02;
  const limit = Number(wet ? rating.wet2 : rating.dry2);
  if (!Number.isFinite(limit) || limit <= 0) return { key: "tractionUnknown", limit: null, wet };
  const fraction = mass / limit;
  return {
    key: fraction <= 0.75 ? "tractionGoodReserve" : fraction <= 1 ? "tractionSufficient" : "tractionInsufficient",
    limit,
    wet,
  };
}
