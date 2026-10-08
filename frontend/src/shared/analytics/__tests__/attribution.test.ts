import { beforeEach, describe, expect, it, vi } from "vitest";
import {
  ATTRIBUTION_STORAGE_KEY,
  GROWTH_UTM_TAXONOMY,
  captureFirstTouchAttribution,
  readFirstTouchAttribution,
} from "../attribution";

function createStorageMock(): Storage {
  const store = new Map<string, string>();
  return {
    get length() {
      return store.size;
    },
    clear: () => {
      store.clear();
    },
    getItem: (key) => store.get(key) ?? null,
    key: (index) => Array.from(store.keys())[index] ?? null,
    removeItem: (key) => void store.delete(key),
    setItem: (key, value) => void store.set(key, value),
  };
}

function setAnalyticsConsent(enabled: boolean) {
  document.cookie = `cookie_consent_v1=${encodeURIComponent(
    JSON.stringify({
      version: "1",
      necessary: true,
      analytics: enabled,
      marketing: false,
      acceptedAt: "2026-07-14T09:00:00.000Z",
    }),
  )};path=/`;
}

const UTM_KEYS = ["utm_source", "utm_medium", "utm_campaign", "utm_term", "utm_content"] as const;
const UNSAFE_CAMPAIGN_VALUES = [
  ["email", "person@example.com"],
  ["phone", "+7 (999) 123-45-67"],
  ["numeric phone", "79991234567"],
  ["hyphenated phone", "7999-123-4567"],
  ["tel-prefixed phone", "tel-7999-123-4567"],
  ["control character", "unsafe\nvalue"],
  ["unsafe separator", "private/value?token"],
] as const;

describe("first-touch growth attribution", () => {
  beforeEach(() => {
    Object.defineProperty(window, "localStorage", {
      value: createStorageMock(),
      configurable: true,
    });
    document.cookie = "cookie_consent_v1=;expires=Thu, 01 Jan 1970 00:00:00 GMT;path=/";
    Object.defineProperty(document, "referrer", { value: "", configurable: true });
    vi.setSystemTime(new Date("2026-07-14T10:00:00.000Z"));
  });

  it("does not capture or persist anything before analytics consent", () => {
    window.history.replaceState({}, "", "/pricing?utm_source=yandex&utm_campaign=launch");

    expect(captureFirstTouchAttribution()).toBeNull();
    expect(window.localStorage.getItem(ATTRIBUTION_STORAGE_KEY)).toBeNull();
  });

  it("persists only approved UTM fields, landing path, referrer host, timestamp, and version", () => {
    setAnalyticsConsent(true);
    window.history.replaceState(
      {},
      "",
      "/pricing?utm_source=yandex&utm_medium=cpc&utm_campaign=launch&utm_term=dotnet&utm_content=hero&email=person%40example.com&gclid=secret",
    );
    Object.defineProperty(document, "referrer", {
      value: "https://search.example/path?q=private",
      configurable: true,
    });

    expect(captureFirstTouchAttribution()).toEqual({
      version: "growth.v1",
      utm_source: "yandex",
      utm_medium: "cpc",
      utm_campaign: "launch",
      utm_term: "dotnet",
      utm_content: "hero",
      landing_path: "/pricing",
      referrer_host: "search.example",
      captured_at: "2026-07-14T10:00:00.000Z",
    });

    const persisted = window.localStorage.getItem(ATTRIBUTION_STORAGE_KEY);
    if (persisted === null) throw new Error("Attribution was not persisted");
    expect(persisted).not.toContain("person@example.com");
    expect(persisted).not.toContain("/path?q=private");
    const parsed: unknown = JSON.parse(persisted);
    if (typeof parsed !== "object" || parsed === null) {
      throw new Error("Persisted attribution is not an object");
    }
    expect(Object.keys(parsed).sort()).toEqual(
      [
        "captured_at",
        "landing_path",
        "referrer_host",
        "version",
        "utm_campaign",
        "utm_content",
        "utm_medium",
        "utm_source",
        "utm_term",
      ].sort(),
    );
  });

  it("preserves the first touch across later calls, including an OTP round trip", () => {
    setAnalyticsConsent(true);
    window.history.replaceState({}, "", "/?utm_source=telegram&utm_campaign=launch");
    const first = captureFirstTouchAttribution();

    window.history.replaceState({}, "", "/login?utm_source=direct&utm_campaign=otp");

    expect(captureFirstTouchAttribution()).toEqual(first);
    expect(readFirstTouchAttribution()).toEqual(first);
  });

  it.each(
    UTM_KEYS.flatMap((key) =>
      UNSAFE_CAMPAIGN_VALUES.map(([kind, value]) => ({ key, kind, value })),
    ),
  )("drops $kind values from $key", ({ key, value }) => {
    setAnalyticsConsent(true);
    const params = new URLSearchParams({ [key]: value });
    window.history.replaceState({}, "", `/?${params}`);

    const attribution = captureFirstTouchAttribution();
    if (attribution === null) throw new Error("Safe attribution was not captured");

    expect(attribution).not.toHaveProperty(key);
    expect(window.localStorage.getItem(ATTRIBUTION_STORAGE_KEY)).not.toContain(value);
  });

  it("keeps safe campaign tokens for every supported UTM key", () => {
    setAnalyticsConsent(true);
    window.history.replaceState(
      {},
      "",
      "/?utm_source=yandex&utm_medium=cpc&utm_campaign=launch&utm_term=dotnet&utm_content=hero",
    );

    expect(captureFirstTouchAttribution()).toMatchObject({
      utm_source: "yandex",
      utm_medium: "cpc",
      utm_campaign: "launch",
      utm_term: "dotnet",
      utm_content: "hero",
    });
  });

  it("publishes the closed UTM taxonomy for campaign conventions", () => {
    expect(GROWTH_UTM_TAXONOMY).toEqual({
      utm_source: [
        "yandex",
        "google",
        "youtube",
        "telegram",
        "github",
        "habr",
        "vc",
        "dzen",
        "linkedin",
        "chatgpt",
        "direct",
        "email",
        "partner",
      ],
      utm_medium: [
        "organic",
        "cpc",
        "paid",
        "social",
        "referral",
        "email",
        "video",
        "ai",
        "partner",
      ],
      utm_campaign: ["launch", "evergreen", "csharp", "dotnet", "aspnetcore"],
      utm_term: ["csharp", "dotnet", "aspnetcore", "backend", "fullstack"],
      utm_content: [
        "hero",
        "header",
        "footer",
        "course",
        "material",
        "pricing",
        "level_test",
        "youtube_description",
        "telegram_post",
      ],
    });
  });

  it("drops unknown token-shaped campaign values", () => {
    setAnalyticsConsent(true);
    window.history.replaceState({}, "", "/?utm_campaign=summer_2026");

    expect(captureFirstTouchAttribution()).not.toHaveProperty("utm_campaign");
  });

  it.each([
    ["/", "/"],
    ["/courses", "/courses"],
    ["/courses/dotnet-fullstack", "/courses/:slug"],
    ["/courses/dotnet-fullstack/learn/019abcdef", "/courses/:slug"],
    ["/knowledge-base", "/knowledge-base"],
    ["/knowledge-base/019abcdef", "/knowledge-base/:id"],
    ["/pricing", "/pricing"],
    ["/pricing/lifetime", "/pricing"],
    ["/level-test", "/level-test"],
    ["/level-test/result/019abcdef", "/level-test"],
    ["/invite/private-token", "/other"],
    ["/users/019abcdef", "/other"],
  ])("stores low-cardinality route template for %s", (path, expected) => {
    setAnalyticsConsent(true);
    window.history.replaceState({}, "", path);

    expect(captureFirstTouchAttribution()).toMatchObject({ landing_path: expected });
    expect(window.localStorage.getItem(ATTRIBUTION_STORAGE_KEY)).not.toContain("019abcdef");
    expect(window.localStorage.getItem(ATTRIBUTION_STORAGE_KEY)).not.toContain("private-token");
  });

  it("does not read stored attribution without current analytics consent", () => {
    window.localStorage.setItem(
      ATTRIBUTION_STORAGE_KEY,
      JSON.stringify({
        version: "growth.v1",
        utm_source: "telegram",
        landing_path: "/",
        captured_at: "2026-07-14T10:00:00.000Z",
      }),
    );
    setAnalyticsConsent(false);

    expect(readFirstTouchAttribution()).toBeNull();
  });
});
