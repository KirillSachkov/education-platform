"use client";

import { useSyncExternalStore } from "react";

/**
 * Detects mobile platform + standalone-mode for PWA install affordance.
 *
 * - `isStandalone`: page is opened from home-screen icon (display-mode: standalone)
 *   OR iOS Safari's nav.standalone is true. When true, install prompt is irrelevant.
 * - `isMobile`: rough heuristic via user-agent (iOS / Android) — used only to gate
 *   mobile-specific install copy. We don't need pixel-perfect detection: desktop
 *   visitors can install too via the URL bar / Chrome menu.
 * - `isIos`: iOS Safari (incl. WebView) — needs a manual «Поделиться → На экран
 *   Домой» instruction since beforeinstallprompt is not supported on iOS.
 *
 * SSR-safe: returns sensible defaults until the browser hydrates.
 */
interface PwaPlatform {
  isStandalone: boolean;
  isMobile: boolean;
  isIos: boolean;
}

const DEFAULT: PwaPlatform = {
  isStandalone: false,
  isMobile: false,
  isIos: false,
};

function detect(): PwaPlatform {
  if (typeof window === "undefined") return DEFAULT;
  const ua = window.navigator.userAgent || "";
  const isIos = /iPhone|iPad|iPod/i.test(ua) && !/CriOS|FxiOS/i.test(ua);
  const isAndroid = /Android/i.test(ua);
  const isMobile = isIos || isAndroid;
  const standaloneMQ =
    typeof window.matchMedia === "function"
      ? window.matchMedia("(display-mode: standalone)").matches
      : false;
  // iOS Safari exposes a non-standard `nav.standalone` for home-screen apps.
  const navStandalone =
    typeof window.navigator !== "undefined" &&
    "standalone" in window.navigator &&
    Boolean((window.navigator as Navigator & { standalone?: boolean }).standalone);
  return {
    isStandalone: standaloneMQ || navStandalone,
    isMobile,
    isIos,
  };
}

// Constant subscribe — the platform doesn't change in-session. We only need
// useSyncExternalStore for SSR-safe hydration.
const subscribe = () => () => {};
const getServerSnapshot = (): PwaPlatform => DEFAULT;

// `detect()` builds a fresh object on every call; returning it directly from
// getSnapshot makes useSyncExternalStore see a new reference each render and
// loop forever ("getSnapshot should be cached"). Platform is stable per
// session, so memoize the first client read into one stable reference.
let clientSnapshot: PwaPlatform | null = null;
const getClientSnapshot = (): PwaPlatform => (clientSnapshot ??= detect());

export function usePwaPlatform(): PwaPlatform {
  return useSyncExternalStore(subscribe, getClientSnapshot, getServerSnapshot);
}
