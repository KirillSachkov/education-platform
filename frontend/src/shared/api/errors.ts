import { translateErrorCode } from "./error-messages";

// Error Types
export const ErrorType = {
  VALIDATION: "VALIDATION",
  NOT_FOUND: "NOT_FOUND",
  FAILURE: "FAILURE",
  CONFLICT: "CONFLICT",
  AUTHENTICATION: "AUTHENTICATION",
  AUTHORIZATION: "AUTHORIZATION",
} as const;

export type ErrorType = (typeof ErrorType)[keyof typeof ErrorType];

// Error Message
export type ErrorMessage = {
  code: string;
  message: string;
  invalidField?: string | null;
};

// API Error
export type ApiError = {
  messages: ErrorMessage[];
  type: ErrorType;
};

// Envelope wrapper для API responses
export type Envelope<T = unknown> = {
  result: T | null;
  error: ApiError | null;
  isError: boolean;
  timeGenerated: string;
};

// Кастомный Error class для Envelope errors
export class EnvelopeError extends Error {
  public readonly apiError: ApiError;
  public readonly type: ErrorType;

  constructor(apiError: ApiError) {
    const firstMessage = apiError.messages[0]?.message ?? "Неизвестная ошибка";
    super(firstMessage);

    this.name = "EnvelopeError";
    this.apiError = apiError;
    this.type = apiError.type;

    // Для корректного instanceof в TypeScript
    Object.setPrototypeOf(this, EnvelopeError.prototype);
  }

  get messages(): ErrorMessage[] {
    return this.apiError.messages;
  }

  get firstMessage(): string {
    return this.apiError.messages[0]?.message ?? "Неизвестная ошибка";
  }

  get allMessages(): string[] {
    return this.apiError.messages.map((m) => m.message);
  }
}

// Ошибка 403 — нет прав доступа
export class ForbiddenError extends Error {
  constructor(message = "Недостаточно прав для выполнения этого действия") {
    super(message);
    this.name = "ForbiddenError";
    Object.setPrototypeOf(this, ForbiddenError.prototype);
  }
}

// Type guard для проверки EnvelopeError
export function isEnvelopeError(error: unknown): error is EnvelopeError {
  return error instanceof EnvelopeError;
}

// Проверка ошибки доступа к контенту (content.access.*)
export function isContentAccessError(error: unknown): error is EnvelopeError {
  return (
    isEnvelopeError(error) &&
    error.messages.some((m) => m.code?.startsWith("content.access."))
  );
}

export function isForbiddenError(error: unknown): error is ForbiddenError {
  return error instanceof ForbiddenError;
}

// Unwrap Envelope — returns result or throws EnvelopeError
export function unwrapEnvelope<T>(envelope: Envelope<T>): T {
  if (envelope.isError || !envelope.result) {
    throw new EnvelopeError(
      envelope.error ?? { messages: [], type: ErrorType.FAILURE },
    );
  }
  return envelope.result;
}

/**
 * Extract the first backend error code (e.g. `trainer.pro.required`) from an
 * EnvelopeError, or `null` for non-envelope errors. Lets callers branch on the
 * stable machine-readable code without re-implementing envelope unwrapping.
 */
export function getErrorCode(error: unknown): string | null {
  if (isEnvelopeError(error)) {
    return error.messages[0]?.code ?? null;
  }
  return null;
}

// Helper to extract error message for toast notifications
export function getErrorMessage(error: unknown, fallback: string): string {
  if (isEnvelopeError(error)) {
    const { code, message } = error.messages[0] ?? {};
    if (code) return translateErrorCode(code, message ?? fallback);
    return message ?? fallback;
  }
  if (isForbiddenError(error)) {
    return error.message;
  }
  if (error instanceof TypeError) {
    return fallback;
  }
  if (error instanceof Error && error.message) {
    return error.message;
  }
  return fallback;
}
