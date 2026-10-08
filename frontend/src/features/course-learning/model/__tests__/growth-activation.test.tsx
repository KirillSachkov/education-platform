import type { ReactNode } from "react";
import { act, renderHook } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { beforeEach, describe, expect, it, vi } from "vitest";

const markMaterialViewed = vi.hoisted(() => vi.fn());
const submitIssue = vi.hoisted(() => vi.fn());
const trackGrowthEvent = vi.hoisted(() => vi.fn());

vi.mock("@/entities/course-progress", () => ({
  courseProgressApi: { markMaterialViewed, submitIssue },
  courseProgressQueryOptions: { baseKey: "course-progress", issueHistoryKey: "issue-history" },
}));
vi.mock("@/entities/enrollment", () => ({ enrollmentQueryOptions: { baseKey: "enrollment" } }));
vi.mock("@/entities/user-progress", () => ({
  userProgressQueryOptions: { baseKey: "user-progress" },
}));
vi.mock("@/shared/analytics", () => ({ trackGrowthEvent }));
vi.mock("@/shared/api", () => ({
  getErrorMessage: (_error: unknown, fallback: string) => fallback,
}));
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

import { useMarkMaterialViewed } from "../use-mark-material-viewed";
import { useSubmitIssue } from "../use-submit-issue";

function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return function Wrapper({ children }: { children: ReactNode }) {
    return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
  };
}

describe("growth activation mutations", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    markMaterialViewed.mockResolvedValue(undefined);
    submitIssue.mockResolvedValue(undefined);
  });

  it("tracks the first completed material only after mark-viewed succeeds", async () => {
    const { result } = renderHook(() => useMarkMaterialViewed("course-1"), {
      wrapper: createWrapper(),
    });

    await act(() => result.current.mutateAsync({ materialId: "material-1" }));

    expect(trackGrowthEvent).toHaveBeenCalledWith(
      {
        name: "first_material_completed",
        properties: { material_id: "material-1", course_id: "course-1" },
      },
      { once: "activation:first-material-completed" },
    );
  });

  it("does not consume course activation for a standalone material completion", async () => {
    const { result } = renderHook(() => useMarkMaterialViewed(), {
      wrapper: createWrapper(),
    });

    await act(() => result.current.mutateAsync({ materialId: "material-1" }));

    expect(trackGrowthEvent).not.toHaveBeenCalled();
  });

  it("tracks the first issue submission only after the backend accepts it", async () => {
    const { result } = renderHook(() => useSubmitIssue("course-1", "issue-1"), {
      wrapper: createWrapper(),
    });

    await act(() => result.current.mutateAsync({ submissionUrl: "https://github.com/a/b/pull/1" }));

    expect(trackGrowthEvent).toHaveBeenCalledWith(
      {
        name: "first_issue_submitted",
        properties: { issue_id: "issue-1", course_id: "course-1" },
      },
      { once: "activation:first-issue-submitted" },
    );
  });
});
