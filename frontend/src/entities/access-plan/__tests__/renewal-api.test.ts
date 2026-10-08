import { beforeEach, describe, expect, it, vi } from "vitest";

const post = vi.hoisted(() => vi.fn());

vi.mock("@/shared/api", () => ({
  apiClient: { post },
}));

import { accessPlanApi } from "../api";

describe("accessPlanApi subscription renewal contract", () => {
  beforeEach(() => {
    post.mockReset();
    post.mockResolvedValue({ data: { result: null } });
  });

  it("cancels renewal through the owner route with a trailing slash", async () => {
    await accessPlanApi.cancelAutoRenewal("grant-1");

    expect(post).toHaveBeenCalledWith("/access/me/grants/grant-1/cancel-renewal/", null);
  });

  it("resumes renewal through the owner route with a trailing slash", async () => {
    await accessPlanApi.resumeAutoRenewal("grant-1");

    expect(post).toHaveBeenCalledWith("/access/me/grants/grant-1/resume-renewal/", null);
  });
});
