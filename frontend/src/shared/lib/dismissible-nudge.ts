"use client";

import { useEffect, useState } from "react";

const DEFAULT_SUPPRESSION_MS = 1000 * 60 * 60 * 24 * 30; // 30 days

// React 19 StrictMode double-mounts effects in dev, which would inflate the
// visit counter on first load. Guard ensures we bump at most once per JS module
// load, per storage key.
const bumpedKeys = new Set<string>();

type StorageProbe = { ok: true; value: number | null } | { ok: false };

function readInt(key: string): StorageProbe {
  try {
    const raw = window.localStorage.getItem(key);
    if (!raw) return { ok: true, value: null };
    const n = Number.parseInt(raw, 10);
    return { ok: true, value: Number.isNaN(n) ? null : n };
  } catch {
    // Storage blocked (e.g. iOS private browsing).
    return { ok: false };
  }
}

function writeInt(key: string, value: number): void {
  try {
    window.localStorage.setItem(key, String(value));
  } catch {
    // ignore — same reason as readInt
  }
}

function bumpVisitCount(key: string): number | null {
  const probe = readInt(key);
  if (!probe.ok) return null; // storage unavailable
  if (bumpedKeys.has(key)) return probe.value ?? 0;
  bumpedKeys.add(key);
  const next = (probe.value ?? 0) + 1;
  writeInt(key, next);
  return next;
}

export type DismissibleNudgeOptions = {
  /** Namespace for the localStorage keys (`<key>:visits`, `<key>:dismissed-at`). */
  storageKey: string;
  /** Minimum page loads before the nudge may appear (first-timers are spared). */
  minVisits?: number;
  /** How long a dismissal suppresses the nudge. Defaults to 30 days. */
  suppressionMs?: number;
  /** Delay before revealing, so we don't pop the instant the page paints. */
  revealDelayMs?: number;
};

export type DismissibleNudge = {
  show: boolean;
  dismiss: () => void;
};

/**
 * Visit-gated, dismiss-suppressed nudge mechanics backed by localStorage — the
 * reusable core behind one-off promo banners (Telegram link, PWA install, …).
 *
 * Rules:
 *  - hidden until the user has loaded the page at least {@link DismissibleNudgeOptions.minVisits}
 *    times (brand-new visitors aren't pestered);
 *  - hidden for {@link DismissibleNudgeOptions.suppressionMs} after a dismissal;
 *  - **fails closed** — if localStorage is unavailable (private mode) the nudge
 *    stays hidden, so we never pester on every load without being able to persist
 *    the dismissal.
 *
 * Returns `show` plus `dismiss()` (persists the suppression timestamp). The caller
 * composes any further conditions on top (e.g. "only when not linked").
 */
export function useDismissibleNudge(options: DismissibleNudgeOptions): DismissibleNudge {
  const {
    storageKey,
    minVisits = 1,
    suppressionMs = DEFAULT_SUPPRESSION_MS,
    revealDelayMs = 0,
  } = options;
  const dismissKey = `${storageKey}:dismissed-at`;
  const visitsKey = `${storageKey}:visits`;
  const [show, setShow] = useState(false);

  useEffect(() => {
    const dismissed = readInt(dismissKey);
    if (!dismissed.ok) return; // storage unavailable → fail closed
    if (dismissed.value !== null && Date.now() - dismissed.value < suppressionMs) return;

    const visits = bumpVisitCount(visitsKey);
    if (visits === null || visits < minVisits) return;

    // Always reveal via a timer (even with a 0ms delay) so we never call setState
    // synchronously in the effect body — keeps the React Compiler lint happy and
    // avoids a cascading render. Also lets the page paint before the nudge pops.
    const handle = window.setTimeout(() => setShow(true), revealDelayMs);
    return () => window.clearTimeout(handle);
  }, [dismissKey, visitsKey, minVisits, suppressionMs, revealDelayMs]);

  function dismiss() {
    writeInt(dismissKey, Date.now());
    setShow(false);
  }

  return { show, dismiss };
}
