"use client";

import { useEffect, useRef } from "react";
import { useCookieConsent } from "@/shared/lib/use-cookie-consent";
import type { GrowthEvent } from "./contract";
import { YANDEX_METRIKA_READY_EVENT } from "./constants";
import { trackGrowthEvent } from "./track-growth-event";

/**
 * Sends one page/surface view per tracking key. On first consent the Metrika
 * script is mounted asynchronously, so a failed immediate send waits for the
 * explicit ready event instead of dropping the acquisition view.
 */
export function useTrackGrowthView(event: GrowthEvent, trackingKey: string, delayMs = 0): void {
  const { consent } = useCookieConsent();
  const eventRef = useRef(event);
  const sentKeyRef = useRef<string | null>(null);

  useEffect(() => {
    eventRef.current = event;
  }, [event]);

  useEffect(() => {
    if (!consent?.analytics || trackingKey.length === 0 || sentKeyRef.current === trackingKey) {
      return;
    }

    let timer: ReturnType<typeof setTimeout> | null = null;
    const send = () => {
      if (sentKeyRef.current === trackingKey) return;
      if (trackGrowthEvent(eventRef.current)) {
        sentKeyRef.current = trackingKey;
        window.removeEventListener(YANDEX_METRIKA_READY_EVENT, send);
      }
    };

    const attempt = () => {
      send();
      if (sentKeyRef.current !== trackingKey) {
        window.addEventListener(YANDEX_METRIKA_READY_EVENT, send);
      }
    };

    if (delayMs > 0) timer = setTimeout(attempt, delayMs);
    else attempt();

    return () => {
      if (timer) clearTimeout(timer);
      window.removeEventListener(YANDEX_METRIKA_READY_EVENT, send);
    };
  }, [consent?.analytics, delayMs, trackingKey]);
}
