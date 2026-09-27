// One measured scroll viewport, one fixed-height content surface. Rows are
// positioned within it; browser scroll anchoring never moves a virtual spacer.
export function listWindow(total, rowHeight, viewportHeight, scrollTop) {
  const contentHeight = total * rowHeight;
  const top = Math.min(
    Math.max(0, scrollTop),
    Math.max(0, contentHeight - viewportHeight),
  );
  const start = Math.max(0, Math.min(total, Math.floor(top / rowHeight) - 3));
  const end = Math.min(
    total,
    Math.ceil((top + viewportHeight) / rowHeight) + 3,
  );
  return { top, start, end, contentHeight };
}
