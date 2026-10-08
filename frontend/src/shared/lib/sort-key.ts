/**
 * Compares two fractional-index sort keys using byte-order (ASCII) comparison.
 *
 * Matches the backend ordering (Postgres `character varying(200) COLLATE "C"`),
 * where uppercase letters sort before lowercase (`'Z' < 'a'`). JavaScript's
 * `String.prototype.localeCompare` uses linguistic ordering and gives the
 * opposite result for mixed-case keys, which caused items with keys like `"Zz"`
 * (generated when moving to the beginning) to end up at the tail in the UI.
 */
export function compareSortKey(a: string, b: string): number {
  if (a < b) return -1;
  if (a > b) return 1;
  return 0;
}

/**
 * Generate a fractional-index sort key strictly between `after` and `before`
 * (either side may be undefined). Used for optimistic cache updates during
 * drag-and-drop — the backend produces the authoritative key; this client-side
 * value only needs to live until the next refetch (~200ms) and preserve the
 * visible ordering during that window.
 */
export function interpolateSortKey(
  after: string | undefined,
  before: string | undefined,
): string {
  if (!after && !before) return "m";
  if (!after) {
    const firstCode = before!.charCodeAt(0);
    return firstCode > 33 ? String.fromCharCode(firstCode - 1) : "";
  }
  if (!before) return after + "m";

  let i = 0;
  while (i < after.length && i < before.length && after[i] === before[i]) i++;

  if (i === after.length) {
    const nextCode = before.charCodeAt(i);
    return after + String.fromCharCode(Math.max(Math.floor(nextCode / 2), 1));
  }

  const afterCode = after.charCodeAt(i);
  const beforeCode = before.charCodeAt(i);
  if (beforeCode - afterCode > 1) {
    return after.slice(0, i) + String.fromCharCode(Math.floor((afterCode + beforeCode) / 2));
  }
  return after + "m";
}
