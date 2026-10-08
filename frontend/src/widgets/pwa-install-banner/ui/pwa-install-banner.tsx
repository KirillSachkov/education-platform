"use client";

import { useEffect, useState } from "react";
import { useInstallPrompt, usePwaPlatform } from "@/shared/lib/pwa";
import { Icons } from "@/shared/ui/icons";
import { Button } from "@/shared/ui/kit/button";
import { cn } from "@/shared/lib/css";

const DISMISS_KEY = "pwa-install-dismissed-at";
const SUPPRESSION_MS = 1000 * 60 * 60 * 24 * 30; // 30 days
const MIN_VISITS_KEY = "pwa-install-visits";
const MIN_VISITS_REQUIRED = 2;
const REVEAL_DELAY_MS = 4000;

function isSuppressed(): boolean {
  try {
    const raw = window.localStorage.getItem(DISMISS_KEY);
    if (!raw) return false;
    const at = Number.parseInt(raw, 10);
    if (Number.isNaN(at)) return false;
    return Date.now() - at < SUPPRESSION_MS;
  } catch {
    // Storage may be blocked (e.g. iOS private browsing) — fail closed
    // (treat as suppressed) so we don't pester the user every page load.
    return true;
  }
}

function suppress(): void {
  try {
    window.localStorage.setItem(DISMISS_KEY, String(Date.now()));
  } catch {
    // ignore — same reason as above
  }
}

// Module-level flag — React 19 StrictMode double-mounts effects in dev, which
// would inflate the visit counter to 2 on first load and prematurely show the
// banner. Guard ensures we increment at most once per JS module load.
let bumpedThisLoad = false;

function bumpVisitCount(): number {
  if (bumpedThisLoad) {
    try {
      const raw = window.localStorage.getItem(MIN_VISITS_KEY);
      const n = raw ? Number.parseInt(raw, 10) : 0;
      return Number.isNaN(n) ? 0 : n;
    } catch {
      return MIN_VISITS_REQUIRED;
    }
  }
  bumpedThisLoad = true;
  try {
    const raw = window.localStorage.getItem(MIN_VISITS_KEY);
    const n = raw ? Number.parseInt(raw, 10) : 0;
    const next = (Number.isNaN(n) ? 0 : n) + 1;
    window.localStorage.setItem(MIN_VISITS_KEY, String(next));
    return next;
  } catch {
    return MIN_VISITS_REQUIRED; // pretend we're past threshold if storage unavailable
  }
}

/**
 * Persistent, dismissable PWA install banner that appears on mobile only.
 *
 * Visibility rules:
 *   1. mobile UA (iOS or Android) AND not already running in standalone mode
 *   2. user has visited the site at least {@link MIN_VISITS_REQUIRED} times — first-
 *      time visitors don't see this; we wait for engagement.
 *   3. user hasn't dismissed in the last {@link SUPPRESSION_MS} window
 *   4. on Android: shows only when `beforeinstallprompt` actually fired
 *   5. on iOS Safari: shows manual «Поделиться → На экран Домой» instructions
 *      (iOS has no programmatic install API)
 *
 * Lives near the bottom on mobile so it stays out of the way until the user
 * is mid-task; bottom-nav has `env(safe-area-inset-bottom)` so we sit above it.
 */
export function PwaInstallBanner() {
  const { isMobile, isIos, isStandalone } = usePwaPlatform();
  const { available, install } = useInstallPrompt();
  const [show, setShow] = useState(false);

  useEffect(() => {
    if (!isMobile || isStandalone) return;
    if (isSuppressed()) return;
    const visits = bumpVisitCount();
    if (visits < MIN_VISITS_REQUIRED) return;
    // Don't pop instantly — let the user start engaging with the page first.
    const handle = window.setTimeout(() => setShow(true), REVEAL_DELAY_MS);
    return () => window.clearTimeout(handle);
  }, [isMobile, isStandalone]);

  if (!show) return null;
  // iOS shows manual instructions; Android needs the native prompt to be available.
  if (!isIos && !available) return null;

  function handleDismiss() {
    suppress();
    setShow(false);
  }

  async function handleInstallAndroid() {
    if (!install) return;
    const outcome = await install();
    if (outcome === "accepted" || outcome === "dismissed") {
      // Suppress regardless — accepted = app installed, dismissed = user said no.
      suppress();
      setShow(false);
    }
  }

  return (
    <div
      role="region"
      aria-labelledby="pwa-install-title"
      className={cn(
        "md:hidden",
        "fixed inset-x-3 z-50",
        "rounded-2xl border border-border/60 bg-card shadow-2xl shadow-black/30",
        "p-4",
        // Lift above mobile-bottom-nav (56px) + safe-area
        "bottom-[calc(env(safe-area-inset-bottom)+72px)]",
      )}
    >
      <div className="flex items-start gap-3">
        <span
          aria-hidden="true"
          className="flex size-10 shrink-0 items-center justify-center rounded-xl bg-primary/12 text-primary"
        >
          <Icons.download className="size-5" />
        </span>
        <div className="min-w-0 flex-1">
          <h2
            id="pwa-install-title"
            className="text-sm font-semibold leading-snug"
          >
            Установить приложение на экран
          </h2>
          {isIos ? (
            <p className="mt-1 text-xs leading-relaxed text-muted-foreground">
              Нажмите{" "}
              <span className="inline-flex items-center gap-0.5 align-middle text-foreground">
                <Icons.shareIos className="size-3.5" />
              </span>{" "}
              «Поделиться» внизу Safari → «На экран Домой». Приложение откроется без браузерных панелей.
            </p>
          ) : (
            <p className="mt-1 text-xs leading-relaxed text-muted-foreground">
              Быстрый запуск с домашнего экрана, push-уведомления, оффлайн-доступ.
            </p>
          )}
        </div>
        <button
          type="button"
          onClick={handleDismiss}
          aria-label="Закрыть подсказку"
          className="-mr-1 -mt-1 inline-flex size-8 items-center justify-center rounded-md text-muted-foreground transition-colors hover:bg-muted hover:text-foreground"
        >
          <Icons.close className="size-4" />
        </button>
      </div>

      {!isIos && (
        <div className="mt-3 flex justify-end">
          <Button
            size="sm"
            onClick={() => {
              void handleInstallAndroid();
            }}
          >
            Установить
          </Button>
        </div>
      )}
    </div>
  );
}
