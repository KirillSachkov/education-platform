import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

const landingGrowthTracker = vi.hoisted(() => vi.fn(() => <span>growth-tracker</span>));

vi.mock("@/features/course-landing/pages/platform", () => ({
  LandingGrowthTracker: landingGrowthTracker,
}));
vi.mock("@/features/landing-header", () => ({ LandingHeader: () => null }));

import SeoLandingLayout from "../layout";

describe("SEO landing layout growth analytics", () => {
  it("mounts the delegated landing and CTA tracker for keyword pages", () => {
    render(
      <SeoLandingLayout>
        <div>SEO content</div>
      </SeoLandingLayout>,
    );

    expect(screen.getByText("growth-tracker")).toBeInTheDocument();
    expect(landingGrowthTracker).toHaveBeenCalled();
    expect(screen.getByRole("main")).toHaveAttribute("id", "main-content");
  });
});
