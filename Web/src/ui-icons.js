// One vector system for navigation and entity rows; no font-dependent glyphs.
import { isSlugModel, locomotiveCatalogKey } from "./locomotive-catalog.js";
const paths = {
  chevron: "M8 4l8 8-8 8",
  map: "M3 5l6-2 6 2 6-2v16l-6 2-6-2-6 2z M9 3v16 M15 5v16",
  locations:
    "M12 21s7-7 7-12a7 7 0 1 0-14 0c0 5 7 12 7 12z M10 9a2 2 0 1 0 4 0a2 2 0 1 0-4 0",
  trains: "M4 15V8h9V4h6v11H4z M7 8V5h3 M14 8h3 M3 19h18 M7 15v3 M17 15v3",
  cars: "M3 7h18v9H3z M7 7v9 M17 7v9 M6 16v3 M18 16v3 M2 20h20",
  signals:
    "M9 2h6a2 2 0 0 1 2 2v11a2 2 0 0 1-2 2H9a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2z M12 6v.1 M12 10v.1 M12 14v.1 M12 17v5 M8 22h8",
  switches:
    "M6 21V3 M10 21V3 M6 21C6 13 12 7 20 5 M10 21C10 15 15 11 22 9 M6 5h4 M6 9h4 M6 19h4 M15 8l2 3 M19 6l2 3",
  turntables:
    "M12 3a9 9 0 1 0 0 18a9 9 0 1 0 0-18 M6 15l9-9 3 3-9 9z M3 12H1 M23 12h-2",
  blocks:
    "M8 5v14 M16 5v14 M6 8h12 M6 12h12 M6 16h12 M4 5V2h16v3 M4 19v3h16v-3",
  routes:
    "M4 6a2 2 0 1 0 4 0a2 2 0 1 0-4 0 M6 8v8a4 4 0 0 0 8 0V8a3 3 0 0 1 6 0v10 M17 15l3 3 3-3",
  jobs: "M6 4H3v18h18V4h-3 M8 2h8v5H8z M7 11h10 M7 16h6",
  players: "M8 6a4 4 0 1 0 8 0a4 4 0 1 0-8 0 M4 22v-3a8 8 0 0 1 16 0v3",
  tracks: "M8 2L5 22 M16 2l3 20 M7 6h10 M6 11h12 M5 16h14 M4 21h16",
  signs: "M12 2l9 9-9 9-9-9z M12 20v3 M12 7v6 M12 16v.1",
  log: "M3 3h14v4 M3 3v18h10 M6 7h7 M6 11h6 M6 15h5 M13 16a5 5 0 1 0 10 0a5 5 0 1 0-10 0 M18 13v3l2 1",
  settings: "M2 6h20 M2 12h20 M2 18h20 M7 3v6 M17 9v6 M10 15v6",
  // The navigation icon is deliberately neutral.  Weather details use the
  // state-specific paths below so the icon agrees with the native snapshot
  // shown beside it (and does not depict clear weather during rain/fog).
  // Only the exposed sun arc is stroked: no hidden disc/ray crosses the
  // transparent cloud interior. All strokes stay inside the 24px viewBox.
  weather: "M4.6 9a3.5 3.5 0 1 1 6.8-1.5 M8 2v1 M2 7h1 M3.7 3.7l.8.8 M12.3 3.7l-.8.8 M6 20h12a4 4 0 0 0 .4-8A5.5 5.5 0 0 0 8 11a4.5 4.5 0 0 0-2 9z",
  // Redrawn from the supplied forecast reference. Silhouettes are centred in
  // the same padded 24-unit box; no hidden moon/sun contours cross a cloud.
  weatherClear: "M12 7.5a4.5 4.5 0 1 0 0 9a4.5 4.5 0 1 0 0-9 M12 2v2 M12 20v2 M2 12h2 M20 12h2 M4.9 4.9l1.4 1.4 M17.7 17.7l1.4 1.4 M19.1 4.9l-1.4 1.4 M6.3 17.7l-1.4 1.4",
  weatherCloudy: "M6.1 17.5h11.2a3.9 3.9 0 0 0 .2-7.8a3.2 3.2 0 0 0-3-1.7A4.7 4.7 0 0 0 5.3 10a3.8 3.8 0 0 0 .8 7.5z",
  weatherRain: "M6.1 14h11.2a3.6 3.6 0 0 0 .2-7.2a3.1 3.1 0 0 0-3-1.6A4.4 4.4 0 0 0 5.3 7a3.5 3.5 0 0 0 .8 7z M9 18l-1.4 3.5 M15.5 18l-1.4 3.5",
  weatherHeavyRain: "M6.1 13.5h11.2a3.6 3.6 0 0 0 .2-7.2a3.1 3.1 0 0 0-3-1.6A4.4 4.4 0 0 0 5.3 6.5a3.5 3.5 0 0 0 .8 7z M7 17l-1.4 4 M10.6 17l-1.4 4 M14.2 17l-1.4 4 M17.8 17l-1.4 4",
  weatherFog: "M5 5h14 M2 9.7h20 M2 14.3h20 M5 19h14",
  weatherThunder: "M6.1 13.3h11.2a3.6 3.6 0 0 0 .2-7.2a3.1 3.1 0 0 0-3-1.6A4.4 4.4 0 0 0 5.3 6.3a3.5 3.5 0 0 0 .8 7z M13.4 14.4l-3.5 4.4h4.4L10.5 23",
  weatherUnknown: "M12 17v.1 M9.5 9a2.5 2.5 0 1 1 4 2c-1.2.8-1.5 1.3-1.5 2",
  weatherClearNight: "M15.2 3a8.8 8.8 0 1 0 5.7 14.4A8.6 8.6 0 0 1 15.2 3z",
  weatherOvercast: "M6.1 17.5h11.2a3.9 3.9 0 0 0 .2-7.8a3.2 3.2 0 0 0-3-1.7A4.7 4.7 0 0 0 5.3 10a3.8 3.8 0 0 0 .8 7.5z",
  weatherPartlyCloudyDay: "M5.1 12A3.2 3.2 0 1 1 10 8 M7.5 1.7v1.5 M1.6 7.6h1.5 M3.2 3.3l1.1 1.1 M11.6 3.3l-1 1.1 M3.3 11.8l-1 .9 M8.1 20h9.4a3.4 3.4 0 0 0 .2-6.8a3 3 0 0 0-2.7-1.6a4.2 4.2 0 0 0-8.2 1.3A3.6 3.6 0 0 0 8.1 20z",
  weatherPartlyCloudyNight: "M18.5 2a4.5 4.5 0 1 0 3.7 6.8A4.7 4.7 0 0 1 18.5 2z M5.5 20h10.8a3.6 3.6 0 0 0 .1-7.2a3.1 3.1 0 0 0-2.8-1.8A4.3 4.3 0 0 0 5.2 12a4 4 0 0 0 .3 8z",
  weatherLightRainDay: "M6.1 14h11.2a3.6 3.6 0 0 0 .2-7.2a3.1 3.1 0 0 0-3-1.6A4.4 4.4 0 0 0 5.3 7a3.5 3.5 0 0 0 .8 7z M9 18l-1.4 3.5 M15.5 18l-1.4 3.5",
  weatherLightRainNight: "M18.8 1.8a3.8 3.8 0 1 0 3.1 5.8a4 4 0 0 1-3.1-5.8z M6.1 15h10a3.2 3.2 0 0 0 .2-6.4a2.8 2.8 0 0 0-2.6-1.5A4 4 0 0 0 6 8a3.5 3.5 0 0 0 .1 7z M8.5 18l-1.3 3 M15 18l-1.3 3",
  weatherLightFogDay: "M17.5 10.8a2.9 2.9 0 0 0-2.5-2.4a2.7 2.7 0 0 0-2.2-1.5A3.5 3.5 0 0 0 6 8.5a2.8 2.8 0 0 0 .5 5.5h10 M2 18h20 M5 22h14",
  weatherLightFogNight: "M13 2.3a4.8 4.8 0 1 0 3.9 7.3A5 5 0 0 1 13 2.3z M5 14h14 M2 18h20 M5 22h14",
  weatherHeavyFog: "M5 5h14 M2 9.7h20 M2 14.3h20 M5 19h14",
};
export function uiIcon(kind) {
  const svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
  svg.setAttribute("viewBox", "0 0 24 24");
  svg.setAttribute("class", "ui-symbol");
  svg.setAttribute("aria-hidden", "true");
  svg.setAttribute("focusable", "false");
  const p = document.createElementNS(svg.namespaceURI, "path");
  p.setAttribute("d", paths[kind] || paths.signs);
  p.setAttribute("fill", "none");
  p.setAttribute("stroke", "currentColor");
  p.setAttribute("stroke-width", kind.startsWith("weather") && kind !== "weather" ? "1.35" : "1.7");
  p.setAttribute("stroke-linecap", "round");
  p.setAttribute("stroke-linejoin", "round");
  svg.append(p);
  return svg;
}

// The sidebar keeps the neutral `trains` navigation path. Rows use compact
// filled catalogue silhouettes, based on the authored booklet miniatures:
// body, cab/boiler profile and real wheel count are all model-specific.
const locomotivePaths = Object.freeze({
  DE2: "M3 10h6V5q0-1 1-1h4q1 0 1 1v5h6q1 0 1 1v4q0 1-1 1H3q-1 0-1-1v-4q0-1 1-1z",
  S060: "M3 10h13V5q0-1 1-1h4q1 0 1 1v10q0 1-1 1H3q-1 0-1-1v-4q0-1 1-1z M5 8h4v2H5z",
  DM3: "M3 10h13V5q0-1 1-1h4q1 0 1 1v10q0 1-1 1H3q-1 0-1-1v-4q0-1 1-1z",
  DH4: "M3 8h7V5q0-1 1-1h3q1 0 1 1v3h6q1 0 1 1v6q0 1-1 1H3q-1 0-1-1V9q0-1 1-1z",
  S282: "M3 8h13V5q0-1 1-1h4q1 0 1 1v9q0 1-1 1H3q-1 0-1-1V9q0-1 1-1z M5 6h3v2H5z",
  S282Tender: "M5 7h14l2 3q1 0 1 1v4q0 1-1 1H3q-1 0-1-1v-4q0-1 1-1z",
  DE6: "M3 7h3V5q0-1 1-1h3q1 0 1 1v2h10q1 0 1 1v7q0 1-1 1H3q-1 0-1-1V8q0-1 1-1z",
  DE6Slug: "M3 11h18q1 0 1 1v3q0 1-1 1H3q-1 0-1-1v-3q0-1 1-1z",
  BE2: "M8 12h1V7q0-1 1-1h4q1 0 1 1v5h1q1 0 1 1v2q0 1-1 1H8q-1 0-1-1v-2q0-1 1-1z",
  DM1U: "M3 5h5q1 0 1 1v5h12q1 0 1 1v4H2V6q0-1 1-1z",
  H1: "M6 14h12q1 0 1 1v1H5v-1q0-1 1-1z M11.5 9h1v5h-1z M7 8.5h10V10H7z",
  Caboose: "M3 7h5V5q0-1 1-1h5q1 0 1 1v2h6q1 0 1 1v7q0 1-1 1H3q-1 0-1-1V8q0-1 1-1z",
  unknown: "M3 8h18q1 0 1 1v6q0 1-1 1H3q-1 0-1-1V9q0-1 1-1z",
});
const locomotiveDetails = Object.freeze({
  DE2: "M10 5h4v4h-4z", S060: "M17 5h4v4h-4z", DM3: "M17 5h4v4h-4z M4 11h2v3H4z",
  DH4: "M11 5h3v3h-3z M3 9h2v5H3z", S282: "M17 5h4v6h-4z",
  S282Tender: "M5 7h14l1 2H4z", DE6: "M7 5h3v3H7z M14 9h2v4h-2z M18 9h2v4h-2z",
  DE6Slug: "M14 12h2v1h-2z M18 12h2v1h-2z", BE2: "M10 7h4v4h-4z",
  DM1U: "M3 8h2v3H3z M6 8h2v3H6z M11 10h10v3H11z",
  Caboose: "M9 5h2v3H9z M12 5h2v3h-2z M4 9h2v2H4z M16 9h2v2h-2z M19 9h2v2h-2z",
});
const locomotiveWheels = Object.freeze({
  DE2: [5, 19], S060: [7, 12, 17], DM3: [7, 12, 17], DH4: [4, 7, 17, 20],
  S282: [2.5, 6, 10, 14, 18, 22], S282Tender: [4, 20], DE6: [4, 7, 17, 20],
  DE6Slug: [4, 7, 17, 20], BE2: [8, 16], DM1U: [7, 19], H1: [7, 17],
  Caboose: [4, 20], unknown: [6, 18],
});
// Miniature body colours, sampled from PDF catalogue pages 65–76. The
// catalogue page/header colour remains car.catalogColor; it is a distinct
// authored palette from the actual miniature (e.g. yellow DM1U header,
// white cab + blue underframe + brown cargo box).
const miniatureColors = Object.freeze({DE2:'#DCA04B',S060:'#63A586',DM3:'#4E91CF',DH4:'#773D3E',S282:'#7A7A79',S282Tender:'#7B7B7A',DE6:'#E57046',BE2:'#DCA04B',DE6Slug:'#E67650',H1:'#8A6557',Caboose:'#773D3E',DM1U:'#BEBAB6'});

function locomotiveModelKey(car) {
  const model = String(car?.catalogModel || car?.model || car?.name || "").toUpperCase();
  if (isSlugModel(model)) return "DE6Slug";
  if (/(?:HANDCAR|DRAISINE|(?:^|[- ])H1(?:[- ]|$))/.test(model)) return "H1";
  if (/(?:S282.*(?:730B|TENDER)|TENDER)/.test(model)) return "S282Tender";
  if (/(?:CABOOSE|SUPPORT)/.test(model)) return "Caboose";
  return locomotiveCatalogKey(model) || "unknown";
}

export function locomotiveIcon(car) {
  const svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
  svg.setAttribute("viewBox", "0 0 24 24");
  svg.setAttribute("class", "ui-symbol locomotive-symbol");
  svg.setAttribute("aria-hidden", "true");
  svg.setAttribute("focusable", "false");
  const key = locomotiveModelKey(car);
  const path = document.createElementNS(svg.namespaceURI, "path");
  path.setAttribute("d", locomotivePaths[key]);
  path.setAttribute("fill", miniatureColors[key] || "currentColor");
  path.setAttribute("fill-rule", "evenodd");
  svg.append(path);
  if (locomotiveDetails[key]) {
    const detail = document.createElementNS(svg.namespaceURI, "path");
    detail.setAttribute("d", locomotiveDetails[key]);
    detail.setAttribute("fill", key === "S282" ? "#ECECE8" : "#25333c");
    detail.setAttribute("fill-opacity", "0.72");
    svg.append(detail);
  }
  if (key === "DM1U") {
    for (const [d,color] of [["M2 13h20v3H2z","#4E91CF"],["M11 10h10v3H11z","#855C4E"]]) {
      const part=document.createElementNS(svg.namespaceURI,"path");part.setAttribute("d",d);part.setAttribute("fill",color);svg.append(part);
    }
  }
  for (const x of locomotiveWheels[key] || locomotiveWheels.unknown) {
    const wheel = document.createElementNS(svg.namespaceURI, "circle");
    wheel.setAttribute("cx", x);
    wheel.setAttribute("cy", "18");
    wheel.setAttribute("r", key === "S282" ? ([2.5, 22].includes(x) ? "1.1" : "1.75") : key === "H1" ? "1.1" : "1.3");
    wheel.setAttribute("fill", "#3c454a");
    wheel.setAttribute("stroke", "#89969e");
    wheel.setAttribute("stroke-width", "0.3");
    svg.append(wheel);
  }
  if (["S060","DM3","S282"].includes(key)) {
    const rod=document.createElementNS(svg.namespaceURI,"path");
    rod.setAttribute("d",key==="S282"?"M6 18h12":"M7 18h10");
    rod.setAttribute("stroke",key==="DM3"?"#DCA04B":"#A94C4C");rod.setAttribute("stroke-width","1.2");svg.append(rod);
  }
  if (/^#[a-f\d]{6}$/i.test(car?.catalogColor || "")) svg.style.color = car.catalogColor;
  return svg;
}
