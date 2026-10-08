import { describe, expect, it } from "vitest";
import { levelTestQueryOptions } from "../api";

describe("level test viewer-scoped query keys", () => {
  it("isolates private latest-attempt caches between authenticated users", () => {
    expect(levelTestQueryOptions.myLatestAttemptKey("user:user-a")).not.toEqual(
      levelTestQueryOptions.myLatestAttemptKey("user:user-b"),
    );
  });

  it("isolates an attempt result across auth and anonymous viewers", () => {
    expect(levelTestQueryOptions.attemptResultKey("attempt-1", "user:user-a")).not.toEqual(
      levelTestQueryOptions.attemptResultKey("attempt-1", "anonymous:anonymous-1"),
    );
  });
});
