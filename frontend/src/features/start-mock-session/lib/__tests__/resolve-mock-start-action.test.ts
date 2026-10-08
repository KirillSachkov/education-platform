import { describe, expect, it } from "vitest";
import { resolveMockStartAction } from "../resolve-mock-start-action";

const base = { isAvailable: true, isAuthenticated: true, hasPro: true, isPending: false };

describe("resolveMockStartAction", () => {
  it("no available questions → unavailable (wins over everything)", () => {
    expect(
      resolveMockStartAction({ ...base, isAvailable: false, isAuthenticated: false, hasPro: false }),
    ).toBe("unavailable");
  });

  it("anonymous → login (before the Pro check, so anon never sees the paywall)", () => {
    expect(resolveMockStartAction({ ...base, isAuthenticated: false, hasPro: false })).toBe("login");
  });

  it("authenticated non-Pro → paywall (#658)", () => {
    expect(resolveMockStartAction({ ...base, hasPro: false })).toBe("paywall");
  });

  it("Pro status still loading (undefined) → start (falls through to server gate)", () => {
    expect(resolveMockStartAction({ ...base, hasPro: undefined })).toBe("start");
  });

  it("Pro + already starting → busy (anti double-click)", () => {
    expect(resolveMockStartAction({ ...base, isPending: true })).toBe("busy");
  });

  it("Pro + idle → start", () => {
    expect(resolveMockStartAction(base)).toBe("start");
  });
});
