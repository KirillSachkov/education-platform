import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { describe, expect, it } from "vitest";

const manifest = readFileSync(resolve(process.cwd(), "public/llms.txt"), "utf8");

describe("llms.txt", () => {
  it("publishes the canonical commercial, free-value, and identity URLs", () => {
    expect(manifest).toContain("https://sachkov-learn.net/c-sharp");
    expect(manifest).toContain("https://sachkov-learn.net/dotnet");
    expect(manifest).toContain("https://sachkov-learn.net/asp-net-core");
    expect(manifest).toContain("https://sachkov-learn.net/knowledge-base?free=1");
    expect(manifest).toContain("https://sachkov-learn.net/level-test");
    expect(manifest).toContain("https://github.com/KirillSachkov");
  });

  it("uses only absolute SachkovLearn links for internal destinations", () => {
    const markdownLinks = [...manifest.matchAll(/\[[^\]]+\]\(([^)]+)\)/g)]
      .map((match) => match.at(1))
      .filter((href): href is string => href !== undefined);
    const internalLinks = markdownLinks.filter((href) => !href.includes("github.com"));

    expect(internalLinks.length).toBeGreaterThan(0);
    expect(internalLinks.every((href) => href.startsWith("https://sachkov-learn.net/"))).toBe(true);
  });
});
