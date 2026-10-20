import { beforeEach, describe, expect, it, vi } from "vitest";
import {
  PRICING_INTENT_STORAGE_KEY,
  PRICING_INTENT_TTL_MS,
  clearPricingIntent,
  parsePricingIntent,
  readPricingIntent,
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

  it("reads a valid legacy intent for an existing payment callback", () => {
    window.sessionStorage.setItem(PRICING_INTENT_STORAGE_KEY, serializedIntent());
    expect(readPricingIntent(NOW)).toEqual({
      intentId: INTENT_ID,
      planSlug: "dotnet-fullstack",
      createdAt: "2026-07-14T10:00:00.000Z",
    });
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
  });
});
