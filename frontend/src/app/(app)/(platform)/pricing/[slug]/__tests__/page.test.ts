import { beforeEach, describe, expect, it, vi } from "vitest";

const { permanentRedirect, redirect } = vi.hoisted(() => ({
  permanentRedirect: vi.fn(),
  redirect: vi.fn(),
}));

vi.mock("next/navigation", () => ({ permanentRedirect, redirect }));

import PricingSlugPage from "../page";

describe("pricing slug canonical redirect", () => {
  beforeEach(() => vi.clearAllMocks());

  it("returns old plan links to the workspace", async () => {
    PricingSlugPage();

    expect(redirect).toHaveBeenCalledWith("/home");
  });
});
