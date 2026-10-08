import { readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";

describe("trainer admin API URL contract", () => {
  it("keeps admin stats endpoints on trailing-slash URLs", () => {
    const currentDir = path.dirname(fileURLToPath(import.meta.url));
    const source = readFileSync(path.join(currentDir, "../api.ts"), "utf8");

    expect(source).toContain("/trainer/admin/stats/?days=");
    expect(source).toContain("/trainer/admin/stats/traffic/?days=");
    expect(source).toContain("/trainer/admin/stats/funnel/?days=");
    expect(source).toContain("/trainer/admin/stats/question-quality/?days=");
    expect(source).toContain("/trainer/admin/stats/topics/?days=");
    expect(source).toContain("/trainer/admin/stats/trends/?days=");
  });
});
