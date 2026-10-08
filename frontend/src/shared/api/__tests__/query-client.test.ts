import { describe, expect, it } from "vitest";
import { ErrorType, EnvelopeError, ForbiddenError } from "../errors";
import { makeQueryClient } from "../query-client";

function retryDecision(error: unknown, failureCount = 0): boolean {
  const retry = makeQueryClient().getDefaultOptions().queries?.retry;
  if (typeof retry !== "function") {
    throw new Error("Retry option is not a function");
  }
  return retry(failureCount, error as Error);
}

describe("makeQueryClient retry policy", () => {
  it("does not retry 403 ForbiddenError", () => {
    expect(retryDecision(new ForbiddenError())).toBe(false);
  });

  it("does not retry envelope business errors", () => {
    expect(
      retryDecision(
        new EnvelopeError({
          type: ErrorType.AUTHORIZATION,
          messages: [{ code: "content.access.denied", message: "Нет доступа" }],
        }),
      ),
    ).toBe(false);
  });

  it("retries transient errors up to three failures", () => {
    expect(retryDecision(new Error("network"), 2)).toBe(true);
    expect(retryDecision(new Error("network"), 3)).toBe(false);
  });
});
