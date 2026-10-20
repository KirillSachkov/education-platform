import { describe, expect, it } from "vitest";
import { resolveWorkspaceCallback } from "../workspace-callback";

describe("workspace callback", () => {
  it.each([
    "/pricing?plan=old&resume=checkout",
    "/pricing/old#buy",
    "/",
    "/dotnet",
    "/trainer/session",
  ])("finishes retired destination %s in the workspace", (raw) => {
    expect(resolveWorkspaceCallback(raw)).toBe("/home");
  });
  it.each([
    "/courses/old/learn/lesson?from=saved#chapter",
    "/invite/old-token",
    "/payment/success?OrderId=old",
  ])("preserves the existing content or settlement destination %s", (raw) => {
    expect(resolveWorkspaceCallback(raw)).toBe(raw);
  });
  it.each([undefined, null, "https://evil.test", "//evil.test", "/\\evil.test"])(
    "rejects an unsafe or missing destination %s",
    (raw) => {
      expect(resolveWorkspaceCallback(raw)).toBe("/home");
    },
  );
});
