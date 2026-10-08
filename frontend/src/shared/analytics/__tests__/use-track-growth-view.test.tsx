import { act, renderHook } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

const trackGrowthEvent = vi.hoisted(() => vi.fn());
const useCookieConsent = vi.hoisted(() => vi.fn());

vi.mock("../track-growth-event", () => ({ trackGrowthEvent }));
vi.mock("@/shared/lib/use-cookie-consent", () => ({ useCookieConsent }));

import { YANDEX_METRIKA_READY_EVENT } from "../constants";
import { useTrackGrowthView } from "../use-track-growth-view";

describe("useTrackGrowthView", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    useCookieConsent.mockReturnValue({ consent: { analytics: true } });
  });

  afterEach(() => vi.useRealTimers());

  it("retries a consented view when Metrika becomes ready and sends it once", () => {
    trackGrowthEvent.mockReturnValueOnce(false).mockReturnValue(true);

    renderHook(() => {
      useTrackGrowthView({ name: "landing_view" }, "landing");
    });
    expect(trackGrowthEvent).toHaveBeenCalledTimes(1);

    act(() => {
      window.dispatchEvent(new Event(YANDEX_METRIKA_READY_EVENT));
    });
    act(() => {
      window.dispatchEvent(new Event(YANDEX_METRIKA_READY_EVENT));
    });

    expect(trackGrowthEvent).toHaveBeenCalledTimes(2);
    expect(trackGrowthEvent).toHaveBeenLastCalledWith({ name: "landing_view" });
  });

  it("does not send or subscribe before analytics consent", () => {
    useCookieConsent.mockReturnValue({ consent: null });

    renderHook(() => {
      useTrackGrowthView({ name: "landing_view" }, "landing");
    });
    act(() => {
      window.dispatchEvent(new Event(YANDEX_METRIKA_READY_EVENT));
    });

    expect(trackGrowthEvent).not.toHaveBeenCalled();
  });

  it("measures delayed engagement only after the configured duration", () => {
    vi.useFakeTimers();
    trackGrowthEvent.mockReturnValue(true);

    renderHook(() => {
      useTrackGrowthView(
        {
          name: "free_material_engaged",
          properties: { material_id: "m-1", engagement: "time_30s" },
        },
        "free-material-engaged:m-1",
        30_000,
      );
    });

    act(() => {
      vi.advanceTimersByTime(29_999);
    });
    expect(trackGrowthEvent).not.toHaveBeenCalled();
    act(() => {
      vi.advanceTimersByTime(1);
    });
    expect(trackGrowthEvent).toHaveBeenCalledTimes(1);
  });
});
