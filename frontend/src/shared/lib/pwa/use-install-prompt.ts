"use client";

import { useEffect, useState, useSyncExternalStore } from "react";

/**
 * Captures the browser's `beforeinstallprompt` event so the app can offer a
 * custom "Install" CTA at a moment of its choosing. The event fires once
 * automatically when install criteria are met (manifest present, served over
 * HTTPS, engagement heuristic passed); calling its `prompt()` method opens the
 * native install dialog. After the user accepts or dismisses, the event
 * becomes single-use — we null it out so the CTA hides until the next page
 * load makes the engagement heuristic re-fire.
 *
 * Returns `{ available, install }`. `install` is `null` until the browser
 * decides the app is installable; when not null, calling it triggers the
 * native prompt and resolves the user's choice.
 *
 * Out of scope: iOS Safari doesn't implement `beforeinstallprompt`. Add-to-
 * home-screen there is a manual menu action; the hook silently returns
 * `available: false`.
 */
interface BeforeInstallPromptEvent extends Event {
  prompt: () => Promise<void>;
  userChoice: Promise<{ outcome: "accepted" | "dismissed"; platform: string }>;
}

type Prompt = BeforeInstallPromptEvent | null;

const subscribers = new Set<() => void>();
let current: Prompt = null;

function setPrompt(next: Prompt) {
  current = next;
  for (const s of subscribers) s();
}

function subscribe(cb: () => void) {
  subscribers.add(cb);
  return () => {
    subscribers.delete(cb);
  };
}

function getSnapshot(): Prompt {
  return current;
}

function getServerSnapshot(): Prompt {
  return null;
}

let listenerInstalled = false;

function installListenerOnce() {
  if (listenerInstalled || typeof window === "undefined") return;
  listenerInstalled = true;
  window.addEventListener("beforeinstallprompt", (e) => {
    e.preventDefault();
    setPrompt(e as BeforeInstallPromptEvent);
  });
  window.addEventListener("appinstalled", () => {
    setPrompt(null);
  });
}

export function useInstallPrompt(): {
  available: boolean;
  install: (() => Promise<"accepted" | "dismissed">) | null;
} {
  // useSyncExternalStore avoids the hydration-mismatch warning that a
  // useState + useEffect pattern would emit when the event has already fired
  // before React hydrated.
  const prompt = useSyncExternalStore(subscribe, getSnapshot, getServerSnapshot);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    installListenerOnce();
  }, []);

  if (!prompt || busy) return { available: false, install: null };

  return {
    available: true,
    install: async () => {
      setBusy(true);
      try {
        await prompt.prompt();
        const choice = await prompt.userChoice;
        if (choice.outcome === "accepted") setPrompt(null);
        return choice.outcome;
      } finally {
        setBusy(false);
      }
    },
  };
}
