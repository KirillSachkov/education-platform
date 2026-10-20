import { describe, expect, it } from "vitest";
import { resolveLockCopy, resolveUnlockHref, resolveSecondaryUnlockHref } from "../lock-copy";

describe("locked content after sales closure", () => {
  it("keeps the exact content destination through sign-in", () => {
    expect(
      resolveUnlockHref({
        lockReason: "anonymous",
        returnTo: "/courses/old/learn/lesson?from=saved",
      }),
    ).toBe("/login?callbackUrl=%2Fcourses%2Fold%2Flearn%2Flesson%3Ffrom%3Dsaved");
  });
  it.each(["trial_required", "standard_required", "plan_required", "not_enrolled"] as const)(
    "takes %s to existing access without a checkout action",
    (lockReason) => {
      expect(resolveUnlockHref({ lockReason })).toBe("/my-plans");
      expect(resolveSecondaryUnlockHref({ lockReason })).toBeNull();
      expect(resolveLockCopy(lockReason).secondaryCta).toBeUndefined();
    },
  );
  it("does not offer anonymous visitors a secondary purchase path", () => {
    expect(resolveSecondaryUnlockHref({ lockReason: "anonymous" })).toBeNull();
    expect(resolveLockCopy("anonymous").secondaryCta).toBeUndefined();
  });
});
