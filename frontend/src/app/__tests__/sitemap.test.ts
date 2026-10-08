import { beforeEach, describe, expect, it, vi } from "vitest";

vi.mock("@/shared/config/site", () => ({
  APP_URL: "https://example.test",
  toAbsoluteUrl: (path: string) => `https://example.test${path}`,
}));

import sitemap from "../sitemap";

describe("sitemap canonical index hygiene", () => {
  beforeEach(() => {
    vi.stubGlobal(
      "fetch",
      vi.fn((input: string | URL | Request) => {
        const url =
          typeof input === "string" ? input : input instanceof URL ? input.href : input.url;
        if (url.includes("/courses/catalog/")) {
          return Promise.resolve(
            Response.json({ result: { items: [{ id: "course", slug: "dotnet" }] } }),
          );
        }
        if (url.includes("/access/plans/public/")) {
          return Promise.resolve(Response.json({ result: [{ slug: "full-access" }] }));
        }
        return Promise.resolve(
          Response.json({
            result: {
              materials: [],
              collections: [{ id: "collection-id", updatedAt: "2026-07-14T00:00:00Z" }],
              roadmaps: [],
            },
          }),
        );
      }),
    );
  });

  it("lists only canonical pricing and collection destinations", async () => {
    const urls = (await sitemap()).map((entry) => entry.url);

    expect(urls).toContain("https://example.test/pricing");
    expect(urls).not.toContain("https://example.test/pricing/full-access");
    expect(urls).not.toContain("https://example.test/collections");
    expect(urls).toContain("https://example.test/collections/collection-id");
  });
});
