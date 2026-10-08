import { describe, expect, it } from "vitest";
import { HUB_PRIMARY_TABS, HUB_TABS, parseHubTab, parseStudySubmode } from "../hub-state";

describe("trainer hub tab contract", () => {
  it("merges Tests into Обучение — no top-level test tab", () => {
    expect(HUB_TABS).toEqual(["study", "mock", "progress", "mistakes", "bookmarks"]);
    expect(HUB_PRIMARY_TABS).toEqual(["study", "mock"]);
    // Старый `?tab=test` (legacy deep-link) откатывается на дефолтную вкладку «Обучение».
    expect(parseHubTab("test")).toBe("study");
  });

  it("keeps Тест as a Study submode", () => {
    expect(parseStudySubmode("test")).toBe("test");
    expect(parseStudySubmode("list")).toBe("list");
    expect(parseStudySubmode("learn")).toBe("learn");
    // Мусор / отсутствие откатываются на дефолтный под-режим.
    expect(parseStudySubmode("nope")).toBe("list");
    expect(parseStudySubmode(null)).toBe("list");
  });
});
