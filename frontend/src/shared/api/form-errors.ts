import type { FieldValues, Path, UseFormSetError } from "react-hook-form";
import { translateErrorCode } from "./error-messages";
import { type EnvelopeError, isEnvelopeError } from "./errors";

const ERROR_CODE_TO_FIELD: Record<string, string> = {
  "auth.password.too_short": "password",
  "auth.password.requires_uppercase": "password",
  "auth.password.requires_lowercase": "password",
  "auth.password.requires_digit": "password",
  "auth.password.requires_special": "password",
  "auth.email_taken": "email",
  "auth.username_taken": "username",
  "auth.invalid_username": "username",
  "auth.invalid_credentials": "email",
};

export function setServerErrors<T extends FieldValues>(
  error: unknown,
  setError: UseFormSetError<T>,
  overrides?: Record<string, Path<T>>,
): boolean {
  if (!isEnvelopeError(error)) return false;
  const msg = (error as EnvelopeError).messages[0];
  if (!msg) return false;
  const field =
    overrides?.[msg.code] ??
    (msg.invalidField as Path<T>) ??
    (ERROR_CODE_TO_FIELD[msg.code] as Path<T>);
  if (!field) return false;
  setError(field, {
    type: "server",
    message: translateErrorCode(msg.code, msg.message),
  });
  return true;
}
