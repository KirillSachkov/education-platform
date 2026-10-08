import { beforeEach, describe, expect, it, vi } from "vitest";

const { permanentRedirect, redirect } = vi.hoisted(() => ({
  permanentRedirect: vi.fn(),
  redirect: vi.fn(),
}));

vi.mock("next/navigation", () => ({ permanentRedirect, redirect }));

import PricingSlugPage from "../page";

describe("pricing slug canonical redirect", () => {
  beforeEach(() => vi.clearAllMocks());

  it("permanently redirects to the canonical pricing page with an encoded anchor", async () => {
    await PricingSlugPage({ params: Promise.resolve({ slug: "full access?x=1" }) });

    expect(permanentRedirect).toHaveBeenCalledWith("/pricing#full%20access%3Fx%3D1");
  });
});
