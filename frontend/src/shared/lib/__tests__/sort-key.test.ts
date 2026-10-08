import { describe, expect, it } from "vitest";
import { compareSortKey, interpolateSortKey } from "../sort-key";

describe("compareSortKey", () => {
  it("returns 0 for equal keys", () => {
    expect(compareSortKey("a", "a")).toBe(0);
    expect(compareSortKey("", "")).toBe(0);
  });

  it("returns a negative number when left is less than right", () => {
    expect(compareSortKey("a", "b")).toBeLessThan(0);
    expect(compareSortKey("a", "aa")).toBeLessThan(0);
  });

  it("returns a positive number when left is greater than right", () => {
    expect(compareSortKey("b", "a")).toBeGreaterThan(0);
    expect(compareSortKey("aa", "a")).toBeGreaterThan(0);
  });

  it("matches byte-order (ASCII) comparison: uppercase sorts before lowercase", () => {
    // This is the critical regression case. JS localeCompare puts lowercase
    // first by default, which caused "Zz" (generated when moving to the start)
    // to end up at the tail of the list.
    expect(compareSortKey("Z", "a")).toBeLessThan(0);
    expect(compareSortKey("Zz", "a")).toBeLessThan(0);
    expect(compareSortKey("a", "Z")).toBeGreaterThan(0);
  });

  it("sorts a list of mixed-case fractional-index keys in byte order", () => {
    const keys = ["a0", "Zz", "m", "A"];
    const sorted = [...keys].sort(compareSortKey);
    expect(sorted).toEqual(["A", "Zz", "a0", "m"]);
  });
});

describe("interpolateSortKey", () => {
  const isBetween = (key: string, after: string | undefined, before: string | undefined) => {
    if (after !== undefined) expect(compareSortKey(after, key)).toBeLessThan(0);
    if (before !== undefined) expect(compareSortKey(key, before)).toBeLessThan(0);
  };

  it("inserts between keys with a gap at the first differing char", () => {
    isBetween(interpolateSortKey("a", "c"), "a", "c");
    isBetween(interpolateSortKey("A", "Z"), "A", "Z");
  });

  it("handles adjacent first chars by extending the suffix", () => {
    isBetween(interpolateSortKey("a", "b"), "a", "b");
    isBetween(interpolateSortKey("aa", "ab"), "aa", "ab");
  });

  it("inserts between when after is a prefix of before (collision regression)", () => {
    // Previous impl returned `before` unchanged for these, causing sort ties.
    isBetween(interpolateSortKey("b", "bm"), "b", "bm");
    isBetween(interpolateSortKey("a", "am"), "a", "am");
    isBetween(interpolateSortKey("a0", "a0m"), "a0", "a0m");
  });

  it("appends a suffix when only after is provided", () => {
    isBetween(interpolateSortKey("b", undefined), "b", undefined);
    isBetween(interpolateSortKey("zz", undefined), "zz", undefined);
  });

  it("decrements the first char when only before is provided", () => {
    isBetween(interpolateSortKey(undefined, "c"), undefined, "c");
    isBetween(interpolateSortKey(undefined, "B"), undefined, "B");
  });

  it("returns a stable placeholder when both ends are undefined", () => {
    expect(interpolateSortKey(undefined, undefined)).toBe("m");
  });
});
