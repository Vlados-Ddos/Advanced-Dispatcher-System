// Reference data transcribed from Derail Valley Maps & Booklets V3.0 (PDF
// vehicle catalogue, pages 65-72). Runtime mass/force values still take
// precedence; these are load-rating references for the displayed grade and
// rain conditions and are never used as invented physics constants.
const ratings = Object.freeze({
  DE2: Object.freeze({ dry0: 1200, dry2: 300, wet2: 250 }),
  S060: Object.freeze({ dry0: 1500, dry2: 400, wet2: 300 }),
  DM3: Object.freeze({ dry0: 2000, dry2: 500, wet2: 400 }),
  DH4: Object.freeze({ dry0: 2000, dry2: 600, wet2: 500 }),
  S282: Object.freeze({ dry0: 3000, dry2: 1000, wet2: 800 }),
  DE6: Object.freeze({ dry0: 3000, dry2: 1200, wet2: 1000 }),
  BE2: Object.freeze({ dry0: 800, dry2: 100, wet2: 50 }),
  DM1U: Object.freeze({ dry0: 0, dry2: 0, wet2: 0 }),
});

export function locomotiveCatalogKey(model) {
  const value = String(model || "").toUpperCase();
  // The slug is visually close to DE6 and its display name starts with the
  // same prefix (DE6-860S). Classify it before the generic DE6 prefix match.
  if (isSlugModel(value)) return null;
  for (const key of Object.keys(ratings)) if (value.startsWith(key)) return key;
  return null;
}

export function isSlugModel(model) {
  const value = String(model || "").toUpperCase().replace(/[ _]+/g, "-");
  return value.includes("SLUG") || /(?:^|-)DE6(?:-?860)?S(?:$|-)/.test(value);
}

export function locomotiveLoadRating(model) {
  const key = locomotiveCatalogKey(model);
  return key ? { key, ...ratings[key] } : null;
}

// A consist's catalogue envelope is additive across the locomotives that are
// actually present.  This is a presentation/reference value only; native
// generated traction remains authoritative for live physics.
export function consistLoadRating(locomotives) {
  const values = (locomotives || [])
    .map((car) => locomotiveLoadRating(car?.catalogModel || car?.model || car?.name))
    .filter(Boolean);
  if (!values.length || values.length !== (locomotives || []).length) return null;
  return {
    key: values.map((value) => value.key).join("+") ,
    dry0: values.reduce((sum, value) => sum + value.dry0, 0),
    dry2: values.reduce((sum, value) => sum + value.dry2, 0),
    wet2: values.reduce((sum, value) => sum + value.wet2, 0),
  };
}
