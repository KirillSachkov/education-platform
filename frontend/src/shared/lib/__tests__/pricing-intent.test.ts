import { beforeEach, describe, expect, it, vi } from "vitest";
import {
  PRICING_INTENT_STORAGE_KEY,
  PRICING_INTENT_TTL_MS,
  buildPricingCheckoutCallback,
  claimPricingAutoResume,
  clearPricingIntent,
  createPricingIntent,
  isMatchingPricingCheckoutIntent,
  parsePricingIntent,
  readPricingIntent,
  readPricingIntentForCallback,
  storePricingIntent,
} from "../pricing-intent";

const INTENT_ID = "0190f4d8-8f6e-7a30-9d8f-4d76f8f86d61";
const NOW = Date.parse("2026-07-14T10:00:00.000Z");

function serializedIntent(overrides: Record<string, unknown> = {}): string {
  return JSON.stringify({
    intentId: INTENT_ID,
    planSlug: "dotnet-fullstack",
    createdAt: "2026-07-14T10:00:00.000Z",
    ...overrides,
  });
}

describe("pricing intent v1", () => {
  beforeEach(() => {
    window.sessionStorage.clear();
    vi.restoreAllMocks();
  });

  it("creates a non-PII intent and an exact same-origin checkout callback", () => {
    vi.spyOn(globalThis.crypto, "randomUUID").mockReturnValue(INTENT_ID);

    const intent = createPricingIntent("dotnet-fullstack", NOW);

    expect(intent).toEqual({
      intentId: INTENT_ID,
      planSlug: "dotnet-fullstack",
      createdAt: "2026-07-14T10:00:00.000Z",
    });
    if (!intent) throw new Error("Expected a valid pricing intent");
    expect(buildPricingCheckoutCallback(intent)).toBe(
      `/pricing?plan=dotnet-fullstack&intent=${INTENT_ID}&resume=checkout`,
    );
  });

  it("stores and reads a valid intent", () => {
    vi.spyOn(globalThis.crypto, "randomUUID").mockReturnValue(INTENT_ID);

    expect(storePricingIntent("dotnet-fullstack", NOW)).toEqual({
      intentId: INTENT_ID,
      planSlug: "dotnet-fullstack",
      createdAt: "2026-07-14T10:00:00.000Z",
    });
    expect(readPricingIntent(NOW)).toEqual({
      intentId: INTENT_ID,
      planSlug: "dotnet-fullstack",
      createdAt: "2026-07-14T10:00:00.000Z",
    });
  });

  it("matches only the exact checkout query for the stored intent", () => {
    const intent = parsePricingIntent(serializedIntent(), NOW);
    if (!intent) throw new Error("Expected a valid pricing intent");

    expect(
      isMatchingPricingCheckoutIntent(
        new URLSearchParams({
          plan: "dotnet-fullstack",
          intent: INTENT_ID,
          resume: "checkout",
        }),
        intent,
      ),
    ).toBe(true);
  });

  it("reads an intent only for its exact sanitized pricing callback", () => {
    window.sessionStorage.setItem(PRICING_INTENT_STORAGE_KEY, serializedIntent());
    const callback = `/pricing?plan=dotnet-fullstack&intent=${INTENT_ID}&resume=checkout`;

    expect(readPricingIntentForCallback(callback, NOW)?.intentId).toBe(INTENT_ID);
    expect(readPricingIntentForCallback(`//evil.com${callback}`, NOW)).toBeNull();
    expect(readPricingIntentForCallback(`${callback}&extra=1`, NOW)).toBeNull();
  });

  it("claims one automatic resume attempt per intent", () => {
    expect(claimPricingAutoResume(INTENT_ID)).toBe(true);
    expect(claimPricingAutoResume(INTENT_ID)).toBe(false);
  });

  it.each([
    ["forged intent", `plan=dotnet-fullstack&intent=${INTENT_ID.slice(0, -1)}2&resume=checkout`],
    ["other plan", `plan=other-plan&intent=${INTENT_ID}&resume=checkout`],
    ["other action", `plan=dotnet-fullstack&intent=${INTENT_ID}&resume=preview`],
    ["duplicate key", `plan=dotnet-fullstack&plan=other&intent=${INTENT_ID}&resume=checkout`],
    [
      "extra key",
      `plan=dotnet-fullstack&intent=${INTENT_ID}&resume=checkout&next=https://evil.com`,
    ],
  ])("rejects a %s query", (_case, query) => {
    const intent = parsePricingIntent(serializedIntent(), NOW);
    if (!intent) throw new Error("Expected a valid pricing intent");

    expect(isMatchingPricingCheckoutIntent(new URLSearchParams(query), intent)).toBe(false);
  });

  it("accepts the TTL boundary and rejects a stale intent", () => {
    expect(parsePricingIntent(serializedIntent(), NOW + PRICING_INTENT_TTL_MS)).not.toBeNull();
    expect(parsePricingIntent(serializedIntent(), NOW + PRICING_INTENT_TTL_MS + 1)).toBeNull();
  });

  it.each([
    ["invalid JSON", "{"],
    ["non-object", JSON.stringify(["not-an-intent"])],
    ["missing field", JSON.stringify({ intentId: INTENT_ID, planSlug: "dotnet-fullstack" })],
    ["extra field", serializedIntent({ email: "person@example.com" })],
    ["uppercase UUID", serializedIntent({ intentId: INTENT_ID.toUpperCase() })],
    ["non-canonical UUID", serializedIntent({ intentId: INTENT_ID.replaceAll("-", "") })],
    ["invalid slug", serializedIntent({ planSlug: "../dotnet" })],
    ["uppercase slug", serializedIntent({ planSlug: "Dotnet-Fullstack" })],
    ["overlong slug", serializedIntent({ planSlug: `a${"b".repeat(80)}` })],
    ["non-canonical timestamp", serializedIntent({ createdAt: "2026-07-14T10:00:00Z" })],
    ["future timestamp", serializedIntent({ createdAt: "2026-07-14T10:00:00.001Z" })],
  ])("rejects %s", (_case, raw) => {
    expect(parsePricingIntent(raw, NOW)).toBeNull();
  });

  it("clears only the matching intent", () => {
    window.sessionStorage.setItem(PRICING_INTENT_STORAGE_KEY, serializedIntent());

    clearPricingIntent("0190f4d8-8f6e-7a30-9d8f-4d76f8f86d62");
    expect(window.sessionStorage.getItem(PRICING_INTENT_STORAGE_KEY)).not.toBeNull();

    clearPricingIntent(INTENT_ID);
    expect(window.sessionStorage.getItem(PRICING_INTENT_STORAGE_KEY)).toBeNull();
    expect(claimPricingAutoResume(INTENT_ID)).toBe(true);
  });
});
