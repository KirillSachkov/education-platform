import { beforeEach, describe, expect, it, vi } from "vitest";
import { ATTRIBUTION_STORAGE_KEY } from "../attribution";
import { YANDEX_METRIKA_READY_EVENT } from "../constants";
import { trackGrowthEvent } from "../track-growth-event";
import { COOKIE_CONSENT_CHANGE_EVENT } from "@/shared/lib/use-cookie-consent";

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

describe("trackGrowthEvent", () => {
  beforeEach(() => {
    Object.defineProperty(window, "localStorage", {
      value: createStorageMock(),
      configurable: true,
    });
    document.cookie = "cookie_consent_v1=;expires=Thu, 01 Jan 1970 00:00:00 GMT;path=/";
    vi.stubEnv("NEXT_PUBLIC_YANDEX_METRIKA_ID", "12345678");
    delete window.ym;
  });

  it("sends a versioned reachGoal with merged first-touch attribution", () => {
    setAnalyticsConsent(true);
    window.localStorage.setItem(
      ATTRIBUTION_STORAGE_KEY,
      JSON.stringify({
        version: "growth.v1",
        utm_source: "telegram",
        landing_path: "/",
        captured_at: "2026-07-14T10:00:00.000Z",
      }),
    );
    const ym = vi.fn();
    window.ym = ym;

    expect(
      trackGrowthEvent({
        name: "course_view",
        properties: { course_id: "course-42" },
      }),
    ).toBe(true);
    expect(ym).toHaveBeenCalledWith(12345678, "reachGoal", "course_view", {
      event_version: "growth.v1",
      utm_source: "telegram",
      landing_path: "/",
      attribution_captured_at: "2026-07-14T10:00:00.000Z",
      course_id: "course-42",
    });
  });

  it.each([
    ["no consent", false, "12345678", true],
    ["invalid counter id", true, "12abc", true],
  ])("does not send with %s", (_case, consent, counterId, hasYm) => {
    setAnalyticsConsent(consent);
    vi.stubEnv("NEXT_PUBLIC_YANDEX_METRIKA_ID", counterId);
    const ym = vi.fn();
    if (hasYm) window.ym = ym;

    expect(trackGrowthEvent({ name: "landing_view" })).toBe(false);
    expect(ym).not.toHaveBeenCalled();
  });

  it("queues a consented event until Metrika becomes ready", () => {
    setAnalyticsConsent(true);

    expect(trackGrowthEvent({ name: "landing_view" })).toBe(true);

    const ym = vi.fn();
    window.ym = ym;
    window.dispatchEvent(new Event(YANDEX_METRIKA_READY_EVENT));

    expect(ym).toHaveBeenCalledWith(
      12345678,
      "reachGoal",
      "landing_view",
      expect.objectContaining({ event_version: "growth.v1", landing_path: "/" }),
    );
  });

  it("drops a queued event when analytics consent is revoked before Metrika is ready", () => {
    setAnalyticsConsent(true);
    expect(trackGrowthEvent({ name: "landing_view" })).toBe(true);

    setAnalyticsConsent(false);
    const ym = vi.fn();
    window.ym = ym;
    window.dispatchEvent(new Event(YANDEX_METRIKA_READY_EVENT));

    expect(ym).not.toHaveBeenCalled();
  });

  it("does not revive a pre-revocation event after analytics consent is granted again", () => {
    setAnalyticsConsent(true);
    expect(trackGrowthEvent({ name: "landing_view" })).toBe(true);

    setAnalyticsConsent(false);
    window.dispatchEvent(new Event(COOKIE_CONSENT_CHANGE_EVENT));
    setAnalyticsConsent(true);
    window.dispatchEvent(new Event(COOKIE_CONSENT_CHANGE_EVENT));

    const ym = vi.fn();
    window.ym = ym;
    window.dispatchEvent(new Event(YANDEX_METRIKA_READY_EVENT));

    expect(ym).not.toHaveBeenCalled();
  });

  it("expires a queued event when Metrika never becomes ready", () => {
    vi.useFakeTimers();
    try {
      setAnalyticsConsent(true);
      expect(trackGrowthEvent({ name: "landing_view" })).toBe(true);

      vi.advanceTimersByTime(30_001);
      const ym = vi.fn();
      window.ym = ym;
      window.dispatchEvent(new Event(YANDEX_METRIKA_READY_EVENT));

      expect(ym).not.toHaveBeenCalled();
    } finally {
      vi.useRealTimers();
    }
  });

  it("deduplicates queued activation events by their explicit once key", () => {
    setAnalyticsConsent(true);
    const event = { name: "first_material_started" as const, properties: { material_id: "m-1" } };

    expect(trackGrowthEvent(event, { once: "activation:first-material" })).toBe(true);
    expect(trackGrowthEvent(event, { once: "activation:first-material" })).toBe(true);

    const ym = vi.fn();
    window.ym = ym;
    window.dispatchEvent(new Event(YANDEX_METRIKA_READY_EVENT));

    expect(ym).toHaveBeenCalledTimes(1);
  });

  it("does not trust malformed analytics consent values", () => {
    document.cookie = `cookie_consent_v1=${encodeURIComponent(
      JSON.stringify({
        version: "1",
        necessary: true,
        analytics: "yes",
        marketing: false,
        acceptedAt: "2026-07-14T09:00:00.000Z",
      }),
    )};path=/`;
    const ym = vi.fn();
    window.ym = ym;

    expect(trackGrowthEvent({ name: "landing_view" })).toBe(false);
    expect(ym).not.toHaveBeenCalled();
  });

  it("captures first touch after consent even while ym is still loading", () => {
    setAnalyticsConsent(true);
    window.history.replaceState({}, "", "/?utm_source=telegram");

    expect(trackGrowthEvent({ name: "landing_view" })).toBe(true);
    const rawAttribution = window.localStorage.getItem(ATTRIBUTION_STORAGE_KEY);
    if (rawAttribution === null) throw new Error("Attribution was not captured");
    expect(JSON.parse(rawAttribution)).toMatchObject({
      version: "growth.v1",
      utm_source: "telegram",
      landing_path: "/",
    });

    window.ym = vi.fn();
    window.dispatchEvent(new Event(YANDEX_METRIKA_READY_EVENT));
  });

  it("deduplicates only when an explicit once key is provided", () => {
    setAnalyticsConsent(true);
    const ym = vi.fn();
    window.ym = ym;
    const event = { name: "first_material_started" as const, properties: { material_id: "m-1" } };

    expect(trackGrowthEvent(event, { once: "activation:first-material" })).toBe(true);
    expect(trackGrowthEvent(event, { once: "activation:first-material" })).toBe(false);
    expect(ym).toHaveBeenCalledTimes(1);
  });

  it("is safe when rendered on the server", () => {
    vi.stubGlobal("window", undefined);

    expect(() => trackGrowthEvent({ name: "landing_view" })).not.toThrow();
    expect(trackGrowthEvent({ name: "landing_view" })).toBe(false);
  });
});
