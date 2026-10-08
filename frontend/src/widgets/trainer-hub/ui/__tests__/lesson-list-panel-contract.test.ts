import { readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";

describe("LessonListPanel test-mode contract", () => {
  it("starts tests as grade-at-end sessions", () => {
    const currentDir = path.dirname(fileURLToPath(import.meta.url));
    const source = readFileSync(path.join(currentDir, "../lesson-list-panel.tsx"), "utf8");

    expect(source).toContain('revealPolicy: "END_OF_SESSION"');
    expect(source).not.toContain('revealPolicy: "PER_QUESTION"');
    expect(source).not.toContain("group-hover:translate-x");
  });
});
