import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

vi.mock("@/features/landing-header", () => ({ LandingHeader: () => null }));

import LandingLayout from "../layout";

describe("marketing landing layout accessibility", () => {
  it("provides the target used by the global skip link", () => {
    render(
      <LandingLayout>
        <div>Landing content</div>
      </LandingLayout>,
    );

    expect(screen.getByRole("main")).toHaveAttribute("id", "main-content");
  });
});
