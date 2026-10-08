import { act, render } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const state = vi.hoisted(() => ({
  analytics: false,
  report: undefined as
    | ((metric: { name: string; value: number; rating: string }) => void)
    | undefined,
}));

vi.mock("next/web-vitals", () => ({
  useReportWebVitals: (
    callback: (metric: { name: string; value: number; rating: string }) => void,
  ) => {
    state.report = callback;
  },
}));

vi.mock("@/shared/lib/use-cookie-consent", () => ({
  readCookieConsent: () => (state.analytics ? { analytics: true } : null),
}));

describe("WebVitalsReporter consent", () => {
  beforeEach(() => {
    vi.resetModules();
    vi.stubEnv("NODE_ENV", "production");
    vi.stubEnv("NEXT_PUBLIC_YANDEX_METRIKA_ID", "12345678");
    state.analytics = false;
    state.report = undefined;
    window.ym = vi.fn();
  });

  it("does not send a finalized web vital after analytics consent is withdrawn", async () => {
    const { WebVitalsReporter } = await import("./web-vitals");
    render(<WebVitalsReporter />);

    act(() => state.report?.({ name: "LCP", value: 2345.4, rating: "good" }));
    expect(window.ym).not.toHaveBeenCalled();

    state.analytics = true;
    act(() => state.report?.({ name: "LCP", value: 2345.4, rating: "good" }));
    expect(window.ym).toHaveBeenCalledWith(12345678, "reachGoal", "webvital.LCP", {
      value: 2345,
      rating: "good",
    });
  });
});
