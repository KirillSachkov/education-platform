import { describe, it, expect, vi, beforeEach } from "vitest";
import { setServerErrors } from "../form-errors";
import { EnvelopeError, ErrorType, type ApiError } from "../errors";

describe("setServerErrors", () => {
  const mockSetError = vi.fn();

  beforeEach(() => {
    mockSetError.mockClear();
  });

  it("returns false for non-EnvelopeError", () => {
    const result = setServerErrors(new Error("random"), mockSetError);
    expect(result).toBe(false);
    expect(mockSetError).not.toHaveBeenCalled();
  });

  it("returns false when error has no messages", () => {
    const apiError: ApiError = { messages: [], type: ErrorType.VALIDATION };
    const err = new EnvelopeError(apiError);
    const result = setServerErrors(err, mockSetError);
    expect(result).toBe(false);
    expect(mockSetError).not.toHaveBeenCalled();
  });

  it("maps auth.password.too_short to password field", () => {
    const apiError: ApiError = {
      messages: [
        {
          code: "auth.password.too_short",
          message: "Пароль должен содержать минимум 8 символов",
        },
      ],
      type: ErrorType.VALIDATION,
    };
    const err = new EnvelopeError(apiError);
    const result = setServerErrors(err, mockSetError);
    expect(result).toBe(true);
    expect(mockSetError).toHaveBeenCalledWith("password", {
      type: "server",
      message: "Пароль должен содержать минимум 8 символов",
    });
  });

  it("maps auth.email_taken to email field", () => {
    const err = new EnvelopeError({
      messages: [
        {
          code: "auth.email_taken",
          message: "Этот email уже используется",
        },
      ],
      type: ErrorType.CONFLICT,
    });
    const result = setServerErrors(err, mockSetError);
    expect(result).toBe(true);
    expect(mockSetError).toHaveBeenCalledWith("email", {
      type: "server",
      message: "Этот email уже используется",
    });
  });

  it("uses overrides when provided", () => {
    const err = new EnvelopeError({
      messages: [
        {
          code: "auth.password.too_short",
          message: "Пароль должен содержать минимум 8 символов",
        },
      ],
      type: ErrorType.VALIDATION,
    });
    const result = setServerErrors(err, mockSetError, {
      "auth.password.too_short": "confirmPassword",
    });
    expect(result).toBe(true);
    expect(mockSetError).toHaveBeenCalledWith("confirmPassword", {
      type: "server",
      message: "Пароль должен содержать минимум 8 символов",
    });
  });

  it("uses invalidField from error message", () => {
    const err = new EnvelopeError({
      messages: [
        {
          code: "value.is.invalid",
          message: "Значение недействительно",
          invalidField: "displayName",
        },
      ],
      type: ErrorType.VALIDATION,
    });
    const result = setServerErrors(err, mockSetError);
    expect(result).toBe(true);
    expect(mockSetError).toHaveBeenCalledWith("displayName", {
      type: "server",
      message: "Значение недействительно",
    });
  });

  it("returns false when no field can be resolved", () => {
    const err = new EnvelopeError({
      messages: [{ code: "SomeUnknownCode", message: "Unknown" }],
      type: ErrorType.FAILURE,
    });
    const result = setServerErrors(err, mockSetError);
    expect(result).toBe(false);
    expect(mockSetError).not.toHaveBeenCalled();
  });
});
