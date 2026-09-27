// Exact producer types: DV JobType and Passenger adapter's PassJobType mapping.
// Unknown extension jobs remain unclassified; no ID or text heuristics.
const paths = {
  PassengerLocal: "M8 6a2 2 0 1 0 4 0a2 2 0 1 0-4 0 M5 19v-4a5 5 0 0 1 10 0v4 M17 5a2 2 0 0 1 0 4 M18 12a4 4 0 0 1 3 4v3",
  PassengerExpress: "M9 5a2 2 0 1 0 4 0a2 2 0 1 0-4 0 M6 18v-4a5 5 0 0 1 10 0v4 M18 10l4 3-4 3 M1 11h3 M1 15h3",
  Transport: "M3 8h18v10H3z M8 8v10 M16 8v10 M6 21h1 M17 21h1 M5 4h14",
  EmptyHaul: "M3 12h18v6H3z M6 21h1 M17 21h1 M3 6h17 M16 3l4 3-4 3",
  ShuntingLoad: "M3 14h18v5H3z M6 22h1 M17 22h1 M12 2v9 M8 7l4 4 4-4 M3 3v6h3 M21 3v6h-3",
  ShuntingUnload: "M3 14h18v5H3z M6 22h1 M17 22h1 M12 11V2 M8 6l4-4 4 4 M3 3v6h3 M21 3v6h-3",
  ComplexTransport: "M2 4h7v7H2z M15 13h7v7h-7z M14 4h6v5 M17 7l3 3 3-3 M10 20H4v-5 M1 17l3-3 3 3",
  unknown: "M4 4h16v16H4z M9 9a3 3 0 0 1 6 0c0 2-3 2-3 4 M12 16v.1",
};
export function jobIconKind(job) {
  return Object.hasOwn(paths, job?.type) ? job.type : "unknown";
}
export function jobIcon(job) {
  const svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
  svg.setAttribute("viewBox", "0 0 24 24");
  svg.setAttribute("class", "job-symbol");
  svg.setAttribute("aria-hidden", "true");
  const path = document.createElementNS(svg.namespaceURI, "path");
  path.setAttribute("d", paths[jobIconKind(job)]);
  path.setAttribute("fill", "none"); path.setAttribute("stroke", "currentColor");
  path.setAttribute("stroke-width", "1.8"); path.setAttribute("stroke-linecap", "round");
  path.setAttribute("stroke-linejoin", "round"); svg.append(path);
  return svg;
}
