"use client";

import { useReportWebVitals } from "next/web-vitals";
import { readCookieConsent } from "@/shared/lib/use-cookie-consent";

interface WebVitalMetric {
  name: string;
  value: number;
  rating: string;
}

declare global {
  interface Window {
    ym?: {
      (id: number, action: "destruct"): void;
      (id: number, action: "init", options: Record<string, unknown>): void;
      (id: number, action: string, target: string, params?: Record<string, unknown>): void;
    };
  }
}

const YANDEX_METRIKA_ID = process.env.NEXT_PUBLIC_YANDEX_METRIKA_ID
  ? Number(process.env.NEXT_PUBLIC_YANDEX_METRIKA_ID)
  : null;

/**
 * Ships Core Web Vitals (LCP / INP / CLS / FCP / TTFB) to Yandex Metrika
 * as `reachGoal("webvital.X")` events with the raw value. Only fires for
 * `production` builds — dev numbers are unstable and noisy.
 */
export function WebVitalsReporter() {
  useReportWebVitals((metric: WebVitalMetric) => {
    if (process.env.NODE_ENV !== "production") return;
    if (!YANDEX_METRIKA_ID) return;
    if (typeof window === "undefined" || typeof window.ym !== "function") return;
    if (!readCookieConsent()?.analytics) return;

    window.ym(YANDEX_METRIKA_ID, "reachGoal", `webvital.${metric.name}`, {
      value: Math.round(metric.value),
      rating: metric.rating,
    });
  });

  return null;
}
