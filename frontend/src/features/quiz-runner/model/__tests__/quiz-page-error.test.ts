import { describe, expect, it } from "vitest";
import { EnvelopeError, ErrorType, ForbiddenError } from "@/shared/api";
import { resolveQuizPageError } from "../quiz-page-error";

function envelopeError(type: ErrorType, code: string): EnvelopeError {
  return new EnvelopeError({
    type,
    messages: [{ code, message: "тест" }],
  });
}

describe("resolveQuizPageError", () => {
  it("404 (NOT_FOUND envelope) → not-found", () => {
    const error = envelopeError(ErrorType.NOT_FOUND, "education.quiz.not.found");
    expect(resolveQuizPageError(error, false)).toEqual({ kind: "not-found" });
    expect(resolveQuizPageError(error, true)).toEqual({ kind: "not-found" });
  });

  it("401 аноним (content.access.unauthorized) → locked/anonymous", () => {
    const error = envelopeError(ErrorType.AUTHENTICATION, "content.access.unauthorized");
    expect(resolveQuizPageError(error, false)).toEqual({
      kind: "locked",
      reason: "anonymous",
    });
  });

  it("403 (generic ForbiddenError без кода) + авторизован → locked/plan_required", () => {
    expect(resolveQuizPageError(new ForbiddenError(), true)).toEqual({
      kind: "locked",
      reason: "plan_required",
    });
  });

  it("403 + аноним (гонка после логаута) → locked/anonymous", () => {
    expect(resolveQuizPageError(new ForbiddenError(), false)).toEqual({
      kind: "locked",
      reason: "anonymous",
    });
  });

  it("content.access.denied envelope (гипотетический путь мимо 403-ветки интерсептора) → locked", () => {
    const error = envelopeError(ErrorType.AUTHORIZATION, "content.access.denied");
    expect(resolveQuizPageError(error, true)).toEqual({
      kind: "locked",
      reason: "plan_required",
    });
  });

  it("прочие ошибки → error", () => {
    expect(resolveQuizPageError(new Error("network down"), true)).toEqual({ kind: "error" });
    expect(
      resolveQuizPageError(envelopeError(ErrorType.VALIDATION, "some.validation"), true),
    ).toEqual({ kind: "error" });
    expect(resolveQuizPageError(undefined, false)).toEqual({ kind: "error" });
  });
});
