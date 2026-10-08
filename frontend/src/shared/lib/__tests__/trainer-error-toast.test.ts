import { readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";

describe("notifyTrainerError", () => {
  it("sends PRO/quota CTA to Trainer Pro, not the generic pricing catalog", () => {
    const currentDir = path.dirname(fileURLToPath(import.meta.url));
    const source = readFileSync(path.join(currentDir, "../trainer-error-toast.ts"), "utf8");

    expect(source).toContain("window.location.assign(routes.trainerPro)");
    expect(source).not.toContain("window.location.assign(routes.pricing)");
  });
});
