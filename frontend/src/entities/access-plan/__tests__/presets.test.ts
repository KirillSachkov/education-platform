import { describe, expect, it } from "vitest";
import { detectPreset, PLAN_PRESETS } from "../presets";
import type { PlanCapability } from "../types";

const FULL = new Set<PlanCapability>([
  "VIEW_MATERIALS",
  "SUBMIT_ISSUES",
  "CODE_REVIEW",
  "COMMUNITY_ACCESS",
  "LIVE_CALLS",
  "JOB_SUPPORT",
]);

describe("PLAN_PRESETS registry (FREE #358 + LEARN_ALL presets removed)", () => {
  it("содержит 2 пресета в правильном порядке", () => {
    expect(PLAN_PRESETS.map((p) => p.preset)).toEqual(["FULL_ALL", "COURSE"]);
  });

  it("FULL_ALL = singleton + полный набор capabilities", () => {
    const spec = PLAN_PRESETS.find((p) => p.preset === "FULL_ALL");
    expect(spec?.tier).toBe("FULL_ALL");
    expect(spec?.isSingleton).toBe(true);
    expect(spec?.capabilities && [...spec.capabilities].sort()).toEqual([...FULL].sort());
  });

  it("LEARN_ALL preset больше не предлагается в Create-форме", () => {
    expect(PLAN_PRESETS.find((p) => p.preset === "COURSE")).toBeDefined();
    expect(PLAN_PRESETS.some((p) => p.tier === "LEARN_ALL")).toBe(false);
  });

  it("COURSE = multiple + null capabilities (пользователь сам)", () => {
    const spec = PLAN_PRESETS.find((p) => p.preset === "COURSE");
    expect(spec?.tier).toBe("COURSE");
    expect(spec?.isSingleton).toBe(false);
    expect(spec?.capabilities).toBeNull();
  });
});

describe("detectPreset(tier)", () => {
  it("FULL_ALL → FULL_ALL preset", () => {
    expect(detectPreset("FULL_ALL")).toBe("FULL_ALL");
  });

  it("LEARN_ALL (legacy archived) → null", () => {
    expect(detectPreset("LEARN_ALL")).toBeNull();
  });

  it("COURSE → COURSE preset", () => {
    expect(detectPreset("COURSE")).toBe("COURSE");
  });

  it("FREE (legacy archived) → null", () => {
    expect(detectPreset("FREE")).toBeNull();
  });

  it("SUBSCRIPTION → null (нет preset'а, dormant)", () => {
    expect(detectPreset("SUBSCRIPTION")).toBeNull();
  });
});
