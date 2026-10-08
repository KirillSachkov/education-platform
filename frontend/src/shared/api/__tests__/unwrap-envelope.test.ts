import { describe, it, expect } from "vitest";
import {
  unwrapEnvelope,
  EnvelopeError,
  ErrorType,
  type Envelope,
} from "../errors";

describe("unwrapEnvelope", () => {
  it("returns result when envelope is valid", () => {
    const envelope: Envelope<{ id: string; name: string }> = {
      isError: false,
      result: { id: "1", name: "test" },
      error: null,
      timeGenerated: "2024-01-01T00:00:00Z",
    };
    expect(unwrapEnvelope(envelope)).toEqual({ id: "1", name: "test" });
  });

  it("throws EnvelopeError when isError is true", () => {
    const envelope: Envelope<string> = {
      isError: true,
      result: null,
      error: {
        messages: [{ code: "ERR", message: "fail" }],
        type: ErrorType.FAILURE,
      },
      timeGenerated: "2024-01-01T00:00:00Z",
    };
    expect(() => unwrapEnvelope(envelope)).toThrow(EnvelopeError);
  });

  it("includes the error message in the thrown EnvelopeError", () => {
    const envelope: Envelope<string> = {
      isError: true,
      result: null,
      error: {
        messages: [{ code: "ERR", message: "Something broke" }],
        type: ErrorType.FAILURE,
      },
      timeGenerated: "2024-01-01T00:00:00Z",
    };
    expect(() => unwrapEnvelope(envelope)).toThrow("Something broke");
  });

  it("throws EnvelopeError when result is null even if isError is false", () => {
    const envelope: Envelope<string> = {
      isError: false,
      result: null,
      error: null,
      timeGenerated: "2024-01-01T00:00:00Z",
    };
    expect(() => unwrapEnvelope(envelope)).toThrow(EnvelopeError);
  });

  it("uses FAILURE type when error is null and result is null", () => {
    const envelope: Envelope<string> = {
      isError: false,
      result: null,
      error: null,
      timeGenerated: "2024-01-01T00:00:00Z",
    };

    try {
      unwrapEnvelope(envelope);
      expect.fail("should have thrown");
    } catch (err) {
      expect(err).toBeInstanceOf(EnvelopeError);
      expect((err as EnvelopeError).type).toBe(ErrorType.FAILURE);
    }
  });

  it("preserves error type from the envelope", () => {
    const envelope: Envelope<string> = {
      isError: true,
      result: null,
      error: {
        messages: [{ code: "NOT_FOUND", message: "Not found" }],
        type: ErrorType.NOT_FOUND,
      },
      timeGenerated: "2024-01-01T00:00:00Z",
    };

    try {
      unwrapEnvelope(envelope);
      expect.fail("should have thrown");
    } catch (err) {
      expect(err).toBeInstanceOf(EnvelopeError);
      expect((err as EnvelopeError).type).toBe(ErrorType.NOT_FOUND);
    }
  });
});
