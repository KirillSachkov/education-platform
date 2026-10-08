import { readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";

describe("AppLayout", () => {
  it("does not clip the desktop global search dropdown", () => {
    const currentDir = path.dirname(fileURLToPath(import.meta.url));
    const source = readFileSync(path.join(currentDir, "../app-layout.tsx"), "utf8");
    const headerClassName = source.match(/<header className="([^"]+)"/)?.[1] ?? "";

    expect(headerClassName).toContain("overflow-hidden");
    expect(headerClassName).toContain("md:overflow-visible");
  });
});
