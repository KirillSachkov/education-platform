import { expect, test } from "vitest";

test("required gate rejects a failing selected suite", () => {
  expect(true).toBe(false);
});
