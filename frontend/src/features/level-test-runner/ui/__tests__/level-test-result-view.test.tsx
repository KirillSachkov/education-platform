import { act, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { LevelTestResultView } from "../level-test-result-view";

interface ClaimOptions {
  onSuccess: (result: { claimedCount: number; latestAttemptId: string | null }) => void;
}

interface TrackedEvent {
  name: string;
}

const mocks = vi.hoisted(() => ({
  sessionStatus: "unauthenticated",
  sessionUserId: null as string | null,
  claimMutate: vi.fn<(request: unknown, options: ClaimOptions) => void>(),
  trackGrowthEvent: vi.fn<(event: TrackedEvent, options?: unknown) => boolean>(),
  attemptResultOptions: vi.fn((attemptId: string, viewerScope: string) => ({
    queryKey: ["level-test", "attempt", attemptId, viewerScope],
  })),
}));

const TEASER = {
  attemptId: "attempt-456",
  overallPercent: 64,
  level: "JUNIOR" as const,
  totalQuestions: 10,
  answeredCount: 9,
  aiGradingStatus: "COMPLETED" as const,
};

vi.mock("@/entities/level-test", () => ({
  DEVELOPER_LEVEL_LABELS: { JUNIOR: "Junior" },
  isAiGradingPending: (status: string) => status === "PENDING",
  isFullLevelTestResult: (result: { sections?: unknown[] }) => Array.isArray(result.sections),
  levelTestQueryOptions: {
    attemptResultOptions: mocks.attemptResultOptions,
  },
}));

vi.mock("@tanstack/react-query", () => ({
  useQuery: () => ({
    data: TEASER,
    isPending: false,
    isError: false,
    isRefetching: false,
    refetch: vi.fn(),
  }),
}));

vi.mock("next-auth/react", () => ({
  useSession: () => ({
    status: mocks.sessionStatus,
    data: mocks.sessionUserId ? { user: { id: mocks.sessionUserId } } : null,
  }),
}));

vi.mock("@/shared/lib/anonymous-id", () => ({
  getOrCreateAnonymousId: () => "anonymous-123",
}));

vi.mock("@/shared/analytics", () => ({
  trackGrowthEvent: mocks.trackGrowthEvent,
}));

vi.mock("../../model/use-claim-level-test-attempts", () => ({
  useClaimLevelTestAttempts: () => ({
    isIdle: true,
    isPending: false,
    mutate: mocks.claimMutate,
  }),
}));

describe("LevelTestResultView analytics and claim flow", () => {
  beforeEach(() => {
    mocks.sessionStatus = "unauthenticated";
    mocks.sessionUserId = null;
    vi.clearAllMocks();
  });

  it("changes the result cache scope when the authenticated identity changes", () => {
    mocks.sessionStatus = "authenticated";
    mocks.sessionUserId = "user-a";
    const view = render(<LevelTestResultView attemptId="attempt-456" />);

    expect(mocks.attemptResultOptions).toHaveBeenLastCalledWith("attempt-456", "user:user-a");

    mocks.sessionUserId = "user-b";
    view.rerender(<LevelTestResultView attemptId="attempt-456" />);

    expect(mocks.attemptResultOptions).toHaveBeenLastCalledWith("attempt-456", "user:user-b");
  });

  it("keeps the exact result callback and tracks teaser/auth once", async () => {
    const user = userEvent.setup();
    const view = render(<LevelTestResultView attemptId="attempt-456" />);

    const loginLink = screen.getByRole("link", { name: "Войти и получить полный разбор" });
    expect(loginLink).toHaveAttribute(
      "href",
      "/login?callbackUrl=%2Flevel-test%2Fresult%2Fattempt-456",
    );
    expect(mocks.trackGrowthEvent).toHaveBeenCalledWith(
      {
        name: "level_test_result_teaser",
        properties: { result_band: "JUNIOR" },
      },
      { once: "level-test-result-teaser:attempt-456" },
    );

    view.rerender(<LevelTestResultView attemptId="attempt-456" />);
    expect(
      mocks.trackGrowthEvent.mock.calls.filter(
        ([event]) => event.name === "level_test_result_teaser",
      ),
    ).toHaveLength(1);

    loginLink.addEventListener("click", (event) => {
      event.preventDefault();
    });
    await user.click(loginLink);
    expect(mocks.trackGrowthEvent).toHaveBeenCalledWith({
      name: "level_test_auth_started",
      properties: {},
    });
    expect(mocks.claimMutate).not.toHaveBeenCalled();
  });

  it("claims after authentication and tracks claimed only after mutation success", () => {
    mocks.sessionStatus = "authenticated";
    mocks.sessionUserId = "user-1";
    const view = render(<LevelTestResultView attemptId="attempt-456" />);

    view.rerender(<LevelTestResultView attemptId="attempt-456" />);

    expect(mocks.claimMutate.mock.calls[0]?.[0]).toEqual({
      anonymousId: "anonymous-123",
      attemptId: "attempt-456",
      viewerScope: "user:user-1",
    });
    expect(mocks.claimMutate.mock.calls[0]?.[1].onSuccess).toEqual(expect.any(Function));
    expect(mocks.claimMutate).toHaveBeenCalledTimes(1);
    expect(
      mocks.trackGrowthEvent.mock.calls.some(([event]) => event.name === "level_test_claimed"),
    ).toBe(false);

    const firstClaimCall = mocks.claimMutate.mock.calls.at(0);
    if (!firstClaimCall) {
      throw new Error("Expected the authenticated attempt to be claimed");
    }
    const claimOptions = firstClaimCall[1];
    act(() => {
      claimOptions.onSuccess({ claimedCount: 1, latestAttemptId: "attempt-456" });
    });

    expect(mocks.trackGrowthEvent).toHaveBeenCalledWith({
      name: "level_test_claimed",
      properties: { result_band: "JUNIOR" },
    });
  });
});
