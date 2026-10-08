import { readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";

describe("trainer API URL contract", () => {
  it("keeps stats endpoints on trailing-slash URLs", () => {
    const currentDir = path.dirname(fileURLToPath(import.meta.url));
    const source = readFileSync(path.join(currentDir, "../api.ts"), "utf8");

    expect(source).toContain("/trainer/stats/activity/?days=");
    expect(source).toContain("/trainer/stats/summary/");
    expect(source).toContain("/trainer/stats/mock-trend/");
    expect(source).toContain("/trainer/stats/strengths/");
    expect(source).toContain("/trainer/stats/trends/");
  });
});
