import { describe, it, expect } from "vitest";
import {
  EnvelopeError,
  ForbiddenError,
  isEnvelopeError,
  isForbiddenError,
  getErrorMessage,
  ErrorType,
  type ApiError,
} from "../errors";

describe("EnvelopeError", () => {
  const apiError: ApiError = {
    messages: [
      {
        code: "auth.password.too_short",
        message: "Пароль должен содержать минимум 8 символов",
      },
      {
        code: "auth.password.requires_uppercase",
        message: "Пароль должен содержать заглавную букву",
      },
    ],
    type: ErrorType.VALIDATION,
  };

  it("constructs with first message as Error.message", () => {
    const err = new EnvelopeError(apiError);
    expect(err.message).toBe("Пароль должен содержать минимум 8 символов");
    expect(err.name).toBe("EnvelopeError");
  });

  it("exposes apiError and type", () => {
    const err = new EnvelopeError(apiError);
    expect(err.apiError).toBe(apiError);
    expect(err.type).toBe(ErrorType.VALIDATION);
  });

  it("firstMessage returns first message text", () => {
    const err = new EnvelopeError(apiError);
    expect(err.firstMessage).toBe(
      "Пароль должен содержать минимум 8 символов",
    );
  });

  it("allMessages returns array of all message texts", () => {
    const err = new EnvelopeError(apiError);
    expect(err.allMessages).toEqual([
      "Пароль должен содержать минимум 8 символов",
      "Пароль должен содержать заглавную букву",
    ]);
  });

  it("messages getter returns the full message objects", () => {
    const err = new EnvelopeError(apiError);
    expect(err.messages).toEqual(apiError.messages);
  });

  it("falls back to 'Unknown error' when messages array is empty", () => {
    const emptyError: ApiError = { messages: [], type: ErrorType.FAILURE };
    const err = new EnvelopeError(emptyError);
    expect(err.message).toBe("Неизвестная ошибка");
    expect(err.firstMessage).toBe("Неизвестная ошибка");
  });

  it("is instanceof EnvelopeError and Error", () => {
    const err = new EnvelopeError(apiError);
    expect(err).toBeInstanceOf(EnvelopeError);
    expect(err).toBeInstanceOf(Error);
  });
});

describe("ForbiddenError", () => {
  it("uses default Russian message", () => {
    const err = new ForbiddenError();
    expect(err.message).toBe(
      "Недостаточно прав для выполнения этого действия",
    );
    expect(err.name).toBe("ForbiddenError");
  });

  it("accepts a custom message", () => {
    const err = new ForbiddenError("Custom forbidden");
    expect(err.message).toBe("Custom forbidden");
  });

  it("is instanceof ForbiddenError and Error", () => {
    const err = new ForbiddenError();
    expect(err).toBeInstanceOf(ForbiddenError);
    expect(err).toBeInstanceOf(Error);
  });
});

describe("isEnvelopeError", () => {
  it("returns true for EnvelopeError instances", () => {
    const err = new EnvelopeError({
      messages: [{ code: "x", message: "y" }],
      type: ErrorType.FAILURE,
    });
    expect(isEnvelopeError(err)).toBe(true);
  });

  it("returns false for plain Error", () => {
    expect(isEnvelopeError(new Error("nope"))).toBe(false);
  });

  it("returns false for ForbiddenError", () => {
    expect(isEnvelopeError(new ForbiddenError())).toBe(false);
  });

  it("returns false for non-error values", () => {
    expect(isEnvelopeError(null)).toBe(false);
    expect(isEnvelopeError("string")).toBe(false);
    expect(isEnvelopeError(42)).toBe(false);
  });
});

describe("isForbiddenError", () => {
  it("returns true for ForbiddenError instances", () => {
    expect(isForbiddenError(new ForbiddenError())).toBe(true);
  });

  it("returns false for plain Error", () => {
    expect(isForbiddenError(new Error("nope"))).toBe(false);
  });

  it("returns false for EnvelopeError", () => {
    const err = new EnvelopeError({
      messages: [{ code: "x", message: "y" }],
      type: ErrorType.FAILURE,
    });
    expect(isForbiddenError(err)).toBe(false);
  });

  it("returns false for non-error values", () => {
    expect(isForbiddenError(undefined)).toBe(false);
    expect(isForbiddenError({})).toBe(false);
  });
});

describe("getErrorMessage", () => {
  const fallback = "Что-то пошло не так";

  it("translates error code for EnvelopeError with known code", () => {
    const err = new EnvelopeError({
      messages: [
        {
          code: "auth.password.too_short",
          message: "Пароль должен содержать минимум 8 символов",
        },
      ],
      type: ErrorType.VALIDATION,
    });
    expect(getErrorMessage(err, fallback)).toBe(
      "Пароль должен содержать минимум 8 символов",
    );
  });

  it("returns original message for EnvelopeError with unknown code", () => {
    const err = new EnvelopeError({
      messages: [{ code: "SomeUnknownCode", message: "Original message" }],
      type: ErrorType.FAILURE,
    });
    expect(getErrorMessage(err, fallback)).toBe("Original message");
  });

  it("returns message for EnvelopeError with no code", () => {
    const err = new EnvelopeError({
      messages: [{ code: "", message: "No code message" }],
      type: ErrorType.FAILURE,
    });
    expect(getErrorMessage(err, fallback)).toBe("No code message");
  });

  it("returns ForbiddenError message", () => {
    const err = new ForbiddenError();
    expect(getErrorMessage(err, fallback)).toBe(
      "Недостаточно прав для выполнения этого действия",
    );
  });

  it("returns plain Error message when present", () => {
    expect(getErrorMessage(new Error("random"), fallback)).toBe("random");
  });

  it("returns fallback for Error with empty message", () => {
    expect(getErrorMessage(new Error(""), fallback)).toBe(fallback);
  });

  it("returns fallback for unknown error types", () => {
    expect(getErrorMessage("string error", fallback)).toBe(fallback);
    expect(getErrorMessage(null, fallback)).toBe(fallback);
  });
});
