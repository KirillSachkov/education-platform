import { describe, expect, it, vi } from "vitest";
vi.mock("@/shared/config/site", () => ({
  APP_URL: "https://example.test",
  toAbsoluteUrl: (path: string) => `https://example.test${path}`,
}));
import sitemap from "../sitemap";

describe("public sitemap", () => {
  it("indexes legal documents without advertising retired sales pages", () => {
    const urls = sitemap().map((entry) => entry.url);
    expect(urls).toContain("https://example.test/legal");
    expect(urls).not.toContain("https://example.test/pricing");
    expect(urls).not.toContain("https://example.test/dotnet");
    expect(
      urls.some((url) =>
        /\/(courses|knowledge-base|collections|level-test|roadmaps|leaderboard|trainer|users|certificates)(\/|$)/.test(
          url,
        ),
      ),
    ).toBe(false);
  });
});
