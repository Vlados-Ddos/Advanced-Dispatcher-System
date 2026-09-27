// One vector system for navigation and entity rows; no font-dependent glyphs.
const paths = {
  chevron: "M8 4l8 8-8 8",
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
  p.setAttribute("stroke-width", "1.7");
  p.setAttribute("stroke-linecap", "round");
  p.setAttribute("stroke-linejoin", "round");
  svg.append(p);
  return svg;
}
