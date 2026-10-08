import { act, renderHook } from "@testing-library/react";
import { beforeEach, describe, expect, it } from "vitest";
import { ATTRIBUTION_STORAGE_KEY, readFirstTouchAttribution } from "@/shared/analytics";
import { useCookieConsent } from "../use-cookie-consent";

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

function setAnalyticsConsent() {
  document.cookie = `cookie_consent_v1=${encodeURIComponent(
    JSON.stringify({
      version: "1",
      necessary: true,
      analytics: true,
      marketing: true,
      acceptedAt: "2026-07-14T09:00:00.000Z",
    }),
  )};path=/`;
}

function seedGrowthStorage() {
  window.localStorage.setItem(
    ATTRIBUTION_STORAGE_KEY,
    JSON.stringify({
      version: "growth.v1",
      utm_source: "telegram",
      landing_path: "/",
      captured_at: "2026-07-14T10:00:00.000Z",
    }),
  );
  window.localStorage.setItem("growth.v1:once:first-material", "1");
  window.localStorage.setItem("growth.v1:future-key", "keep-private-to-growth");
  window.localStorage.setItem("unrelated-key", "keep");
}

describe("growth analytics consent revocation", () => {
  beforeEach(() => {
    Object.defineProperty(window, "localStorage", {
      value: createStorageMock(),
      configurable: true,
    });
    document.cookie = "cookie_consent_v1=;expires=Thu, 01 Jan 1970 00:00:00 GMT;path=/";
    setAnalyticsConsent();
    seedGrowthStorage();
  });

  it.each(["necessary-only", "custom-disabled", "reset"] as const)(
    "clears every growth.v1 key on %s and re-consent does not restore attribution",
    (action) => {
      const { result } = renderHook(() => useCookieConsent());

      act(() => {
        if (action === "necessary-only") result.current.acceptNecessaryOnly();
        if (action === "custom-disabled") {
          result.current.acceptCustom({ analytics: false, marketing: true });
        }
        if (action === "reset") result.current.reset();
      });

      expect(window.localStorage.getItem(ATTRIBUTION_STORAGE_KEY)).toBeNull();
      expect(window.localStorage.getItem("growth.v1:once:first-material")).toBeNull();
      expect(window.localStorage.getItem("growth.v1:future-key")).toBeNull();
      expect(window.localStorage.getItem("unrelated-key")).toBe("keep");

      act(() => {
        result.current.acceptAll();
      });

      expect(readFirstTouchAttribution()).toBeNull();
    },
  );
});
