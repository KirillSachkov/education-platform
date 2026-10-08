import { act, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { LevelTestFunnel } from "../level-test-funnel";

interface SubmitOptions {
  onSuccess: (result: { attemptId: string }) => void;
}

interface TrackedEvent {
  name: string;
}

const mocks = vi.hoisted(() => ({
  sessionStatus: "unauthenticated",
  sessionUserId: null as string | null,
  push: vi.fn(),
  mutate: vi.fn<(request: unknown, options: SubmitOptions) => void>(),
  getAnonymousId: vi.fn(() => "anonymous-123"),
  trackGrowthEvent: vi.fn<(event: TrackedEvent, options?: unknown) => boolean>(),
  runnerProps: null as Record<string, unknown> | null,
}));

const TEST = {
  id: "test-123",
  title: "Level test",
  totalQuestions: 1,
  sections: [],
  questions: [
    {
      id: "question-1",
      text: "Question",
      type: "SINGLE_CHOICE",
      section: null,
      difficulty: null,
      options: [{ id: "option-1", text: "Answer" }],
    },
  ],
};

const SUBMIT_ANSWERS = [{ questionId: "question-1", selectedOptionIds: ["option-1"] }];

interface RunnerProps extends Record<string, unknown> {
  initialAnswers?: unknown;
  initialIndex?: number;
  onSubmit: (answers: typeof SUBMIT_ANSWERS) => void;
}

const VALID_DRAFT = {
  answers: {
    "question-1": { selectedOptionIds: ["option-1"], textAnswer: "" },
  },
  index: 0,
  updatedAt: "2026-07-14T10:00:00.000Z",
};

const anonymousDraftKey = "level-test-draft:test-123:anonymous:anonymous-123";
const userDraftKey = (userId: string) => `level-test-draft:test-123:user:${userId}`;
const lastAttemptKey = (ownerScope: string) => `level-test:last-attempt-id:${ownerScope}`;
const anonymousLastAttemptKey = lastAttemptKey("anonymous:anonymous-123");
const userLastAttemptKey = (userId: string) => lastAttemptKey(`user:${userId}`);

function createStorage() {
  const values = new Map<string, string>();
  return {
    clear: () => {
      values.clear();
    },
    getItem: (key: string) => values.get(key) ?? null,
    removeItem: (key: string) => values.delete(key),
    setItem: (key: string, value: string) => values.set(key, value),
  };
}

vi.mock("@/entities/level-test", () => ({
  levelTestQueryOptions: {
    activeLevelTestOptions: () => ({ queryKey: ["level-test", "active"] }),
    myLatestAttemptOptions: () => ({ queryKey: ["level-test", "latest"] }),
  },
}));

vi.mock("@tanstack/react-query", () => ({
  useQuery: (options: { queryKey: string[] }) =>
    options.queryKey.includes("active")
      ? { data: TEST, isPending: false, isError: false, refetch: vi.fn() }
      : { data: null, isPending: false, isError: false },
}));

vi.mock("next-auth/react", () => ({
  useSession: () => ({
    status: mocks.sessionStatus,
    data: mocks.sessionStatus === "authenticated" ? { user: { id: mocks.sessionUserId } } : null,
  }),
}));

vi.mock("next/navigation", () => ({
  useRouter: () => ({ push: mocks.push }),
}));

vi.mock("@/shared/lib/anonymous-id", () => ({
  getOrCreateAnonymousId: mocks.getAnonymousId,
}));

vi.mock("@/shared/analytics", () => ({
  trackGrowthEvent: mocks.trackGrowthEvent,
}));

vi.mock("../../model/use-submit-level-test-attempt", () => ({
  useSubmitLevelTestAttempt: () => ({ isPending: false, mutate: mocks.mutate }),
}));

vi.mock("../level-test-landing-hero", () => ({
  LevelTestLandingHero: ({
    lastAttemptId,
    onStart,
  }: {
    lastAttemptId: string | null;
    onStart: () => void;
  }) => (
    <>
      {lastAttemptId ? <span>Last attempt: {lastAttemptId}</span> : null}
      <button type="button" onClick={onStart}>
        Start test
      </button>
    </>
  ),
}));

vi.mock("../level-test-runner", async () => {
  const { useState } = await import("react");

  return {
    LevelTestRunner: (props: RunnerProps) => {
      const [mountedAnswers] = useState(props.initialAnswers);
      mocks.runnerProps = { ...props, mountedAnswers };
      return (
        <div>
          <span>Runner index: {props.initialIndex ?? 0}</span>
          <button
            type="button"
            onClick={() => {
              props.onSubmit(SUBMIT_ANSWERS);
            }}
          >
            Submit test
          </button>
        </div>
      );
    },
  };
});

describe("LevelTestFunnel anonymous flow", () => {
  beforeEach(() => {
    mocks.sessionStatus = "unauthenticated";
    mocks.sessionUserId = null;
    mocks.runnerProps = null;
    vi.clearAllMocks();
    vi.stubGlobal("sessionStorage", createStorage());
  });

  it("starts the test for an anonymous visitor without routing to login", async () => {
    const user = userEvent.setup();
    const view = render(<LevelTestFunnel />);

    await user.click(screen.getByRole("button", { name: "Start test" }));
    view.rerender(<LevelTestFunnel />);

    expect(screen.getByText("Runner index: 0")).toBeInTheDocument();
    expect(mocks.push).not.toHaveBeenCalled();
    expect(mocks.trackGrowthEvent).toHaveBeenCalledWith({
      name: "level_test_started",
      properties: { test_id: "test-123" },
    });
    expect(
      mocks.trackGrowthEvent.mock.calls.filter(([event]) => event.name === "level_test_started"),
    ).toHaveLength(1);
  });

  it.each([
    ["authenticated user", "authenticated", "user-a", userLastAttemptKey("user-a")],
    ["anonymous visitor", "unauthenticated", null, anonymousLastAttemptKey],
  ] as const)("restores the same %s's last attempt after reload", (_case, status, userId, key) => {
    mocks.sessionStatus = status;
    mocks.sessionUserId = userId;
    sessionStorage.setItem(key, "attempt-user-a");

    const view = render(<LevelTestFunnel />);

    expect(screen.getByText("Last attempt: attempt-user-a")).toBeInTheDocument();

    view.unmount();
    render(<LevelTestFunnel />);

    expect(screen.getByText("Last attempt: attempt-user-a")).toBeInTheDocument();
  });

  it.each([
    ["another authenticated user", "authenticated", "user-b"],
    ["an anonymous visitor", "unauthenticated", null],
  ] as const)(
    "does not expose user A's last attempt after switching to %s",
    (_case, status, userId) => {
      sessionStorage.setItem("level-test:last-attempt-id", "attempt-user-a");
      sessionStorage.setItem(userLastAttemptKey("user-a"), "attempt-user-a");
      mocks.sessionStatus = "authenticated";
      mocks.sessionUserId = "user-a";
      const view = render(<LevelTestFunnel />);

      expect(screen.getByText("Last attempt: attempt-user-a")).toBeInTheDocument();

      mocks.sessionStatus = status;
      mocks.sessionUserId = userId;
      view.rerender(<LevelTestFunnel />);

      expect(screen.queryByText("Last attempt: attempt-user-a")).not.toBeInTheDocument();
    },
  );

  it("restores the same anonymous scope's draft after reload", () => {
    sessionStorage.setItem(anonymousDraftKey, JSON.stringify(VALID_DRAFT));

    const view = render(<LevelTestFunnel />);

    expect(screen.getByText("Runner index: 0")).toBeInTheDocument();
    expect(mocks.runnerProps?.initialAnswers).toEqual({
      "question-1": { selectedOptionIds: ["option-1"], textAnswer: "" },
    });
    expect(mocks.push).not.toHaveBeenCalled();

    view.unmount();
    render(<LevelTestFunnel />);

    expect(screen.getByText("Runner index: 0")).toBeInTheDocument();
    expect(mocks.runnerProps?.initialAnswers).toEqual(VALID_DRAFT.answers);
  });

  it.each([
    ["another authenticated user", "authenticated", "user-b"],
    ["an anonymous visitor", "unauthenticated", null],
  ] as const)("does not restore user A's draft for %s", (_case, status, userId) => {
    sessionStorage.setItem(userDraftKey("user-a"), JSON.stringify(VALID_DRAFT));
    mocks.sessionStatus = status;
    mocks.sessionUserId = userId;

    render(<LevelTestFunnel />);

    expect(screen.getByRole("button", { name: "Start test" })).toBeInTheDocument();
    expect(screen.queryByText(/Runner index:/)).not.toBeInTheDocument();
  });

  it("drops restored answers when the authenticated user changes", () => {
    sessionStorage.setItem(userDraftKey("user-a"), JSON.stringify(VALID_DRAFT));
    mocks.sessionStatus = "authenticated";
    mocks.sessionUserId = "user-a";
    const view = render(<LevelTestFunnel />);

    expect(mocks.runnerProps?.initialAnswers).toEqual(VALID_DRAFT.answers);

    mocks.sessionUserId = "user-b";
    view.rerender(<LevelTestFunnel />);

    expect(mocks.runnerProps?.initialAnswers).toBeUndefined();
    expect(mocks.runnerProps?.mountedAnswers).toBeUndefined();
  });

  it.each([
    ["string index", { ...VALID_DRAFT, index: "corrupt" }],
    ["NaN index", { ...VALID_DRAFT, index: Number.NaN }],
    ["out-of-range index", { ...VALID_DRAFT, index: TEST.questions.length }],
    ["negative index", { ...VALID_DRAFT, index: -1 }],
    ["fractional index", { ...VALID_DRAFT, index: 0.5 }],
    ["answers array", { ...VALID_DRAFT, answers: [] }],
    ["answer entry array", { ...VALID_DRAFT, answers: { "question-1": [] } }],
    [
      "non-string selected option",
      {
        ...VALID_DRAFT,
        answers: {
          "question-1": { selectedOptionIds: ["option-1", 42], textAnswer: "" },
        },
      },
    ],
    [
      "non-string text answer",
      {
        ...VALID_DRAFT,
        answers: { "question-1": { selectedOptionIds: [], textAnswer: null } },
      },
    ],
    ["invalid updatedAt", { ...VALID_DRAFT, updatedAt: "not-a-date" }],
  ])("ignores a malformed draft with %s", (_case, draft) => {
    sessionStorage.setItem(anonymousDraftKey, JSON.stringify(draft));

    render(<LevelTestFunnel />);

    expect(screen.getByRole("button", { name: "Start test" })).toBeInTheDocument();
    expect(screen.queryByText(/Runner index:/)).not.toBeInTheDocument();
  });

  it("submits the anonymous id and tracks completion only after success", async () => {
    const user = userEvent.setup();
    let onSuccess: ((result: { attemptId: string }) => void) | undefined;
    mocks.mutate.mockImplementation((_request, options) => {
      onSuccess = options.onSuccess;
    });
    render(<LevelTestFunnel />);

    await user.click(screen.getByRole("button", { name: "Start test" }));
    await user.click(screen.getByRole("button", { name: "Submit test" }));

    expect(mocks.mutate.mock.calls[0]?.[0]).toEqual({
      quizId: "test-123",
      anonymousId: "anonymous-123",
      answers: SUBMIT_ANSWERS,
      viewerScope: "anonymous:anonymous-123",
    });
    expect(mocks.mutate.mock.calls[0]?.[1].onSuccess).toEqual(expect.any(Function));
    expect(
      mocks.trackGrowthEvent.mock.calls.some(([event]) => event.name === "level_test_completed"),
    ).toBe(false);
    expect(mocks.push).not.toHaveBeenCalled();

    act(() => onSuccess?.({ attemptId: "attempt-456" }));

    expect(mocks.trackGrowthEvent).toHaveBeenCalledWith({
      name: "level_test_completed",
      properties: { test_id: "test-123" },
    });
    expect(sessionStorage.getItem(anonymousLastAttemptKey)).toBe("attempt-456");
    expect(sessionStorage.getItem("level-test:last-attempt-id")).toBeNull();
    expect(mocks.push).toHaveBeenCalledWith("/level-test/result/attempt-456");
  });
});
