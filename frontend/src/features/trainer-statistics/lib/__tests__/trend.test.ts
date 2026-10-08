import { describe, expect, it } from "vitest";

import { deltaTone, formatDelta } from "../trend";

describe("deltaTone", () => {
  it("maps sign to tone and null to none", () => {
    expect(deltaTone(5)).toBe("up");
    expect(deltaTone(-3)).toBe("down");
    expect(deltaTone(0)).toBe("flat");
    expect(deltaTone(null)).toBe("none");
  });
});

describe("formatDelta", () => {
  it("formats growth, drop, flat and missing history", () => {
    expect(formatDelta(7)).toBe("+7");
    expect(formatDelta(-4)).toBe("−4"); // U+2212 minus
    expect(formatDelta(0)).toBe("0");
    expect(formatDelta(null)).toBe("новая");
  });
});
