import { describe, it, expect } from "vitest";
import { translateErrorCode } from "../error-messages";

describe("translateErrorCode", () => {
  it("returns Russian translation for known auth password code", () => {
    expect(translateErrorCode("auth.password.too_short", "fallback")).toBe(
      "Пароль должен содержать минимум 8 символов",
    );
  });

  it("returns translation for dotted auth codes", () => {
    expect(translateErrorCode("auth.invalid_credentials", "fallback")).toBe(
      "Неверный email или пароль",
    );
  });

  it("returns fallback for unknown code", () => {
    expect(translateErrorCode("NonExistentCode", "my fallback")).toBe(
      "my fallback",
    );
  });

  it("returns translation for auth.email_taken", () => {
    expect(translateErrorCode("auth.email_taken", "fallback")).toBe(
      "Этот email уже используется",
    );
  });

  it("returns translation for auth.username_taken", () => {
    expect(translateErrorCode("auth.username_taken", "fallback")).toBe(
      "Это имя пользователя уже занято",
    );
  });
});
