import { fireEvent, render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const trackGrowthEvent = vi.hoisted(() => vi.fn());
const useTrackGrowthView = vi.hoisted(() => vi.fn());
const usePathname = vi.hoisted(() => vi.fn(() => "/"));

vi.mock("next/navigation", () => ({ usePathname }));

vi.mock("@/shared/analytics", () => ({
  GROWTH_CTA_PLACEMENTS: {
    hero_full_access: "hero",
    hero_program: "hero",
    final_consultation: "footer",
  },
  trackGrowthEvent,
  useTrackGrowthView,
}));

import { LandingGrowthTracker } from "../landing-growth-tracker";

describe("LandingGrowthTracker", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    usePathname.mockReturnValue("/");
  });

  it("tracks the landing view and delegated primary CTA clicks", () => {
    render(
      <>
        <LandingGrowthTracker />
        <a data-growth-cta="hero_full_access" data-growth-placement="hero" href="#price">
          <span>Выбрать доступ</span>
        </a>
      </>,
    );

    expect(useTrackGrowthView).toHaveBeenCalledWith({ name: "landing_view" }, "landing:/");
    fireEvent.click(screen.getByText("Выбрать доступ"));
    expect(trackGrowthEvent).toHaveBeenCalledWith({
      name: "cta_click",
      properties: { cta_id: "hero_full_access", placement: "hero" },
    });
  });

  it("ignores unknown CTA ids and invalid id/placement combinations", () => {
    render(
      <>
        <LandingGrowthTracker />
        <button data-growth-cta="dynamic" data-growth-placement="hero">
          Unknown CTA
        </button>
        <button data-growth-cta="hero_full_access" data-growth-placement="footer">
          Mismatched CTA
        </button>
      </>,
    );

    fireEvent.click(screen.getByText("Unknown CTA"));
    fireEvent.click(screen.getByText("Mismatched CTA"));
    expect(trackGrowthEvent).not.toHaveBeenCalled();
  });

  it("uses a pathname-specific view key across keyword landing navigation", () => {
    usePathname.mockReturnValue("/c-sharp");
    const view = render(<LandingGrowthTracker />);
    expect(useTrackGrowthView).toHaveBeenLastCalledWith(
      { name: "landing_view" },
      "landing:/c-sharp",
    );

    usePathname.mockReturnValue("/dotnet");
    view.rerender(<LandingGrowthTracker />);
    expect(useTrackGrowthView).toHaveBeenLastCalledWith(
      { name: "landing_view" },
      "landing:/dotnet",
    );
  });
});
