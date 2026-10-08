import { readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";

describe("trainer admin content API URL contract", () => {
  it("keeps admin endpoints on trailing-slash URLs", () => {
    const currentDir = path.dirname(fileURLToPath(import.meta.url));
    const source = readFileSync(path.join(currentDir, "../api.ts"), "utf8");

    expect(source).toContain("/trainer/topics/manage/");
    expect(source).toContain("/manage/");
    expect(source).toContain("/banks/");
    expect(source).toContain("/publish/");
    expect(source).toContain("/unpublish/");
    expect(source).toContain("/tier/");
  });
});
