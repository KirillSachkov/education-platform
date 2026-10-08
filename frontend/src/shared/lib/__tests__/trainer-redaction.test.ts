import { describe, expect, it } from "vitest";
import { isTrainerContentRedacted } from "../trainer-redaction";

describe("isTrainerContentRedacted", () => {
  it("true когда вопрос заблокирован (даже если контент почему-то есть)", () => {
    expect(isTrainerContentRedacted(true, "Что такое DI?")).toBe(true);
    expect(isTrainerContentRedacted(true, null)).toBe(true);
  });

  it("true для редактированного контента: null / undefined / пустого / только-пробелы", () => {
    expect(isTrainerContentRedacted(false, null)).toBe(true);
    expect(isTrainerContentRedacted(false, undefined)).toBe(true);
    expect(isTrainerContentRedacted(false, "")).toBe(true);
    expect(isTrainerContentRedacted(false, "   \n\t")).toBe(true);
  });

  it("false для разблокированного вопроса с реальным контентом", () => {
    expect(isTrainerContentRedacted(false, "Объясни принципы SOLID")).toBe(false);
    expect(isTrainerContentRedacted(false, "0")).toBe(false);
  });
});
