"use client";

import { useSyncExternalStore } from "react";

const COOKIE_NAME = "cookie_consent_v1";
const VERSION = "1";
const COOKIE_TTL_DAYS = 365;
export const COOKIE_CONSENT_CHANGE_EVENT = "cookie-consent-change";
const GROWTH_STORAGE_PREFIX = "growth.v1:";

export type ConsentCategory = "necessary" | "analytics" | "marketing";

export interface CookieConsent {
  version: string;
  necessary: true;
  analytics: boolean;
  marketing: boolean;
  acceptedAt: string;
}

function readRawCookie(): string | null {
  if (typeof document === "undefined") return null;
  const match = document.cookie.match(new RegExp(`(^|;\\s*)${COOKIE_NAME}=([^;]+)`));
  return match ? match[2] : null;
}

export function parseCookieConsent(raw: string | null): CookieConsent | null {
  if (raw === null) return null;
  try {
    const parsed = JSON.parse(decodeURIComponent(raw)) as Partial<CookieConsent> | null;
    if (
      parsed === null ||
      parsed.version !== VERSION ||
      parsed.necessary !== true ||
      typeof parsed.analytics !== "boolean" ||
      typeof parsed.marketing !== "boolean" ||
      typeof parsed.acceptedAt !== "string"
    ) {
      return null;
    }
    return parsed as CookieConsent;
  } catch {
    return null;
  }
}

export function readCookieConsent(): CookieConsent | null {
  return parseCookieConsent(readRawCookie());
}

function clearGrowthAnalyticsStorage() {
  if (typeof window === "undefined") return;
  try {
    const keys: string[] = [];
    for (let index = 0; index < window.localStorage.length; index += 1) {
      const key = window.localStorage.key(index);
      if (key?.startsWith(GROWTH_STORAGE_PREFIX)) keys.push(key);
    }
    for (const key of keys) window.localStorage.removeItem(key);
  } catch {
    // Consent changes must still work when localStorage is blocked.
  }
}

function writeCookie(consent: CookieConsent) {
  if (!consent.analytics) clearGrowthAnalyticsStorage();
  const value = encodeURIComponent(JSON.stringify(consent));
  const expires = new Date();
  expires.setDate(expires.getDate() + COOKIE_TTL_DAYS);
  const secureAttr =
    typeof window !== "undefined" && window.location.protocol === "https:" ? ";Secure" : "";
  document.cookie = `${COOKIE_NAME}=${value};expires=${expires.toUTCString()};path=/;SameSite=Lax${secureAttr}`;
  window.dispatchEvent(new Event(COOKIE_CONSENT_CHANGE_EVENT));
}

const subscribe = (callback: () => void) => {
  if (typeof window === "undefined") return () => {};
  window.addEventListener(COOKIE_CONSENT_CHANGE_EVENT, callback);
  return () => {
    window.removeEventListener(COOKIE_CONSENT_CHANGE_EVENT, callback);
  };
};

export function useCookieConsent() {
  const raw = useSyncExternalStore(subscribe, readRawCookie, () => null);
  const consent = parseCookieConsent(raw);

  function acceptAll() {
    writeCookie({
      version: VERSION,
      necessary: true,
      analytics: true,
      marketing: true,
      acceptedAt: new Date().toISOString(),
    });
  }

  function acceptNecessaryOnly() {
    writeCookie({
      version: VERSION,
      necessary: true,
      analytics: false,
      marketing: false,
      acceptedAt: new Date().toISOString(),
    });
  }

  function acceptCustom(opts: { analytics: boolean; marketing: boolean }) {
    writeCookie({
      version: VERSION,
      necessary: true,
      analytics: opts.analytics,
      marketing: opts.marketing,
      acceptedAt: new Date().toISOString(),
    });
  }

  function reset() {
    clearGrowthAnalyticsStorage();
    const secureAttr = window.location.protocol === "https:" ? ";Secure" : "";
    document.cookie = `${COOKIE_NAME}=;expires=Thu, 01 Jan 1970 00:00:00 GMT;path=/;SameSite=Lax${secureAttr}`;
    window.dispatchEvent(new Event(COOKIE_CONSENT_CHANGE_EVENT));
  }

  return {
    consent,
    hasDecided: consent !== null,
    acceptAll,
    acceptNecessaryOnly,
    acceptCustom,
    reset,
  };
}
