import { act, renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { PropsWithChildren } from "react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const submitAttempt = vi.hoisted(() => vi.fn());
const claimAttempts = vi.hoisted(() => vi.fn());

vi.mock("@/entities/level-test", () => ({
  levelTestApi: { submitAttempt, claimAttempts },
  levelTestQueryOptions: {
    attemptResultKey: (attemptId: string, viewerScope: string) => [
      "level-test",
      "attempts",
      attemptId,
      "result",
      viewerScope,
    ],
  },
}));

vi.mock("sonner", () => ({ toast: { error: vi.fn() } }));

import { useClaimLevelTestAttempts } from "../use-claim-level-test-attempts";
import { useSubmitLevelTestAttempt } from "../use-submit-level-test-attempt";

function setup() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  const wrapper = ({ children }: PropsWithChildren) => (
    <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  );
  return { queryClient, wrapper };
}

describe("level test mutation cache scope", () => {
  beforeEach(() => vi.clearAllMocks());

  it("seeds the result key from the submit variables captured at mutation start", async () => {
    const response = { attemptId: "attempt-a", level: "JUNIOR" };
    submitAttempt.mockResolvedValue(response);
    const { queryClient, wrapper } = setup();
    const view = renderHook(() => useSubmitLevelTestAttempt(), { wrapper });

    act(() => {
      view.result.current.mutate({
        quizId: "quiz-1",
        anonymousId: null,
        answers: [],
        viewerScope: "user:user-a",
      });
    });
    await waitFor(() => {
      expect(view.result.current.isSuccess).toBe(true);
    });

    expect(
      queryClient.getQueryData(["level-test", "attempts", "attempt-a", "result", "user:user-a"]),
    ).toEqual(response);
    expect(
      queryClient.getQueryData(["level-test", "attempts", "attempt-a", "result", "user:user-b"]),
    ).toBeUndefined();
  });

  it("invalidates the result key from the claim variables captured at mutation start", async () => {
    claimAttempts.mockResolvedValue({ claimedCount: 1, latestAttemptId: "attempt-a" });
    const { queryClient, wrapper } = setup();
    const invalidate = vi.spyOn(queryClient, "invalidateQueries").mockResolvedValue();
    const view = renderHook(() => useClaimLevelTestAttempts(), { wrapper });

    act(() => {
      view.result.current.mutate({
        anonymousId: "anonymous-a",
        attemptId: "attempt-a",
        viewerScope: "user:user-a",
      });
    });
    await waitFor(() => {
      expect(view.result.current.isSuccess).toBe(true);
    });

    expect(invalidate).toHaveBeenCalledWith({
      queryKey: ["level-test", "attempts", "attempt-a", "result", "user:user-a"],
    });
  });
});
