import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { describe, expect, it } from "vitest";

const manifest = readFileSync(resolve(process.cwd(), "public/llms.txt"), "utf8");

describe("llms.txt", () => {
  it("points visitors to sign-in and legal documents, without advertising new purchases", () => {
    expect(manifest).toContain("https://sachkov-learn.net/login");
    expect(manifest).toContain("https://sachkov-learn.net/legal");
    expect(manifest).not.toContain("https://sachkov-learn.net/pricing");
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
