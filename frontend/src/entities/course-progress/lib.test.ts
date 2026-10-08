import { describe, expect, it } from "vitest";
import { isCertificateEligible } from "./lib";
import type { CourseLearningSummaryDto } from "./types";

function summary(overrides: Partial<CourseLearningSummaryDto>): CourseLearningSummaryDto {
  return {
    totalModules: 0,
    materialsTotal: 0,
    materialsViewed: 0,
    modulesCompleted: 0,
    issuesTotal: 0,
    issuesCompleted: 0,
    totalItems: 10,
    completedItems: 0,
    progressPercent: 0,
    ...overrides,
  };
}

describe("isCertificateEligible", () => {
  it("выдаёт при ровно 80%", () => {
    expect(isCertificateEligible(summary({ progressPercent: 80 }))).toBe(true);
  });

  it("выдаёт при 100%", () => {
    expect(isCertificateEligible(summary({ progressPercent: 100 }))).toBe(true);
  });

  it("не выдаёт при 79% (чуть ниже порога)", () => {
    expect(isCertificateEligible(summary({ progressPercent: 79 }))).toBe(false);
  });

  it("не выдаёт при 0%", () => {
    expect(isCertificateEligible(summary({ progressPercent: 0 }))).toBe(false);
  });

  it("не выдаёт для пустого курса даже при progressPercent=100", () => {
    expect(isCertificateEligible(summary({ totalItems: 0, progressPercent: 100 }))).toBe(false);
  });
});
