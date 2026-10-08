import { COOKIE_CONSENT_CHANGE_EVENT, readCookieConsent } from "@/shared/lib/use-cookie-consent";
import { captureFirstTouchAttribution } from "./attribution";
import { YANDEX_METRIKA_READY_EVENT } from "./constants";
import { GROWTH_EVENT_VERSION, normalizeGrowthEventProperties, type GrowthEvent } from "./contract";

declare global {
  interface Window {
    __sachkovMetrikaInitialized?: boolean;
    ym?: {
      (id: number, action: "destruct"): void;
      (id: number, action: "init", options: Record<string, unknown>): void;
      (id: number, action: string, target: string, params?: Record<string, unknown>): void;
    };
  }
}

const ONCE_KEY_PREFIX = `${GROWTH_EVENT_VERSION}:once:`;
const MAX_ONCE_KEY_LENGTH = 100;
const ONCE_KEY_PATTERN = /^[a-zA-Z0-9._:-]+$/;
const METRIKA_READY_QUEUE_TTL_MS = 30_000;

export interface TrackGrowthEventOptions {
  once?: string;
}

function readCounterId(): number | null {
  const raw = process.env.NEXT_PUBLIC_YANDEX_METRIKA_ID;
  if (!raw || !/^\d+$/.test(raw)) return null;
  const id = Number(raw);
  return Number.isSafeInteger(id) && id > 0 ? id : null;
}

function getOnceStorageKey(once: string): string | null {
  if (once.length === 0 || once.length > MAX_ONCE_KEY_LENGTH || !ONCE_KEY_PATTERN.test(once)) {
    return null;
  }
  return `${ONCE_KEY_PREFIX}${once}`;
}

function queueUntilMetrikaReady(event: GrowthEvent, options: TrackGrowthEventOptions): boolean {
  const cleanup = () => {
    window.removeEventListener(YANDEX_METRIKA_READY_EVENT, handleReady);
    window.removeEventListener(COOKIE_CONSENT_CHANGE_EVENT, handleConsentChange);
    window.clearTimeout(timeoutId);
  };
  const handleReady = () => {
    cleanup();
    trackGrowthEvent(event, options);
  };
  const handleConsentChange = () => {
    if (!readCookieConsent()?.analytics) cleanup();
  };
  const timeoutId = window.setTimeout(cleanup, METRIKA_READY_QUEUE_TTL_MS);

  window.addEventListener(YANDEX_METRIKA_READY_EVENT, handleReady);
  window.addEventListener(COOKIE_CONSENT_CHANGE_EVENT, handleConsentChange);
  return true;
}

export function trackGrowthEvent(
  event: GrowthEvent,
  options: TrackGrowthEventOptions = {},
): boolean {
  if (typeof window === "undefined" || typeof document === "undefined") return false;
  if (!readCookieConsent()?.analytics) return false;

  const attribution = captureFirstTouchAttribution();
  const counterId = readCounterId();
  if (counterId === null) return false;
  if (typeof window.ym !== "function") {
    return queueUntilMetrikaReady(event, options);
  }

  let onceStorageKey: string | null = null;
  if (options.once !== undefined) {
    onceStorageKey = getOnceStorageKey(options.once);
    if (onceStorageKey === null) return false;
    try {
      if (window.localStorage.getItem(onceStorageKey) === "1") return false;
      window.localStorage.setItem(onceStorageKey, "1");
    } catch {
      return false;
    }
  }

  const params: Record<string, unknown> = { event_version: GROWTH_EVENT_VERSION };
  if (attribution) {
    const { version: _version, captured_at, ...safeAttribution } = attribution;
    Object.assign(params, safeAttribution, { attribution_captured_at: captured_at });
  }
  Object.assign(params, normalizeGrowthEventProperties(event));

  try {
    window.ym(counterId, "reachGoal", event.name, params);
    return true;
  } catch {
    if (onceStorageKey) {
      try {
        window.localStorage.removeItem(onceStorageKey);
      } catch {
        // Ignore cleanup failure: analytics must never break the caller.
      }
    }
    return false;
  }
}
