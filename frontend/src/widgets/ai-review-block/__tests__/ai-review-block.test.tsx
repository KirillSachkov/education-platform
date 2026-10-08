import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import type * as ReactQuery from "@tanstack/react-query";

import { EnvelopeError } from "@/shared/api";
import type * as AiReviewEntity from "@/entities/ai-review";
import type { AiReviewDetailDto } from "@/entities/ai-review";
import { AiReviewBlock } from "../ai-review-block";

// Управляем состоянием запроса детерминированно, без сети (#718 / #15).
let mockQuery: {
  isPending: boolean;
  isError: boolean;
  error?: unknown;
  data?: AiReviewDetailDto;
};

vi.mock("@tanstack/react-query", async (importOriginal) => {
  const actual = await importOriginal<typeof ReactQuery>();
  return { ...actual, useQuery: () => mockQuery };
});

// Дочерние блоки не важны для проверки состояний; мутацию ре-ревью (#725) заглушаем,
// чтобы не тянуть QueryClientProvider.
vi.mock("@/entities/ai-review", async (importOriginal) => {
  const actual = await importOriginal<typeof AiReviewEntity>();
  return {
    ...actual,
    IterationTimeline: () => <div data-testid="iteration-timeline" />,
    useStudentRerunReview: () => ({ mutate: vi.fn(), isPending: false }),
  };
});

vi.mock("@/features/finalize-submission", () => ({
  useFinalizeSubmission: () => ({ mutate: vi.fn(), isPending: false }),
}));

vi.mock("@/features/iteration-feedback", () => ({
  IterationFeedbackButtons: () => null,
}));

function envelopeError(code: string): EnvelopeError {
  return new EnvelopeError({
    type: code === "review.not_found" ? "NOT_FOUND" : "FAILURE",
    messages: [{ code, message: "боль" }],
  });
}

function makeReview(overrides: Partial<AiReviewDetailDto> = {}): AiReviewDetailDto {
  return {
    id: "r1",
    submissionId: "s1",
    status: "READY",
    pullRequestUrl: "https://github.com/owner/repo/pull/1",
    pullNumber: 1,
    repoFullName: "owner/repo",
    iterations: [],
    ...overrides,
  } as AiReviewDetailDto;
}

function makeIteration(
  verdict: AiReviewDetailDto["latestVerdict"],
): AiReviewDetailDto["iterations"][number] {
  return {
    id: "it1",
    iterationNumber: 1,
    status: "COMPLETED",
    verdict,
  } as AiReviewDetailDto["iterations"][number];
}

describe("AiReviewBlock states (#718)", () => {
  beforeEach(() => {
    mockQuery = { isPending: false, isError: false };
  });

  it("shows a loading state while pending", () => {
    mockQuery = { isPending: true, isError: false };
    render(<AiReviewBlock submissionId="s1" isOpen isCompleted={false} />);
    expect(screen.getByText(/Загружаем состояние AI-проверки/)).toBeInTheDocument();
  });

  it("shows an explicit «not started» state on a review.not_found 404 (not an error)", () => {
    mockQuery = { isPending: false, isError: true, error: envelopeError("review.not_found") };
    render(<AiReviewBlock submissionId="s1" isOpen isCompleted={false} />);

    expect(screen.getByText(/AI-проверка ещё не запущена для этой попытки/)).toBeInTheDocument();
    expect(screen.queryByText(/Не удалось загрузить статус AI-проверки/)).toBeNull();
  });

  it("shows a real error state for a non-404 failure (distinguished from 404)", () => {
    mockQuery = { isPending: false, isError: true, error: envelopeError("server.error") };
    render(<AiReviewBlock submissionId="s1" isOpen isCompleted={false} />);

    expect(screen.getByText(/Не удалось загрузить статус AI-проверки/)).toBeInTheDocument();
    expect(screen.queryByText(/AI-проверка ещё не запущена для этой попытки/)).toBeNull();
  });

  it("renders the review block when the AI review exists", () => {
    mockQuery = { isPending: false, isError: false, data: makeReview({ status: "READY" }) };
    render(<AiReviewBlock submissionId="s1" isOpen isCompleted={false} />);
    expect(screen.getByTestId("ai-review-block")).toBeInTheDocument();
  });

  it("still surfaces the FAILED failure reason (no_installation) in the guidance banner", () => {
    // #718 — не ломаем чтение lastIteration.failureReason.
    mockQuery = {
      isPending: false,
      isError: false,
      data: makeReview({
        status: "FAILED",
        iterations: [
          {
            id: "it1",
            iterationNumber: 1,
            status: "FAILED",
            failureReason: "review.no_installation",
          } as AiReviewDetailDto["iterations"][number],
        ],
      }),
    };
    render(<AiReviewBlock submissionId="s1" isOpen isCompleted={false} />);

    expect(screen.getByTestId("guidance-banner")).toHaveTextContent("GitHub App не подключён");
  });
});

describe("AiReviewBlock — доработка после MINOR-approve (#725)", () => {
  beforeEach(() => {
    mockQuery = { isPending: false, isError: false };
  });

  it("shows «Проверить снова» when the task is completed with MINOR_ISSUES", () => {
    mockQuery = {
      isPending: false,
      isError: false,
      data: makeReview({
        status: "READY",
        latestVerdict: "MINOR_ISSUES",
        iterations: [makeIteration("MINOR_ISSUES")],
      }),
    };
    render(<AiReviewBlock submissionId="s1" isOpen={false} isCompleted />);

    expect(screen.getByTestId("student-rerun-button")).toBeInTheDocument();
    // Копирайт баннера приглашает доработать, а не «переотправлять не нужно».
    expect(screen.getByTestId("guidance-banner")).toHaveTextContent(
      /переотправлять не обязательно/,
    );
  });

  it("hides «Проверить снова» when the task was accepted cleanly (LOOKS_GOOD)", () => {
    mockQuery = {
      isPending: false,
      isError: false,
      data: makeReview({
        status: "READY",
        latestVerdict: "LOOKS_GOOD",
        iterations: [makeIteration("LOOKS_GOOD")],
      }),
    };
    render(<AiReviewBlock submissionId="s1" isOpen={false} isCompleted />);

    expect(screen.queryByTestId("student-rerun-button")).toBeNull();
  });

  it("hides «Проверить снова» when the submission is still open (not completed)", () => {
    // MINOR-вердикт, но задание ещё не зачтено (edge) — доработка не предлагается.
    mockQuery = {
      isPending: false,
      isError: false,
      data: makeReview({
        status: "READY",
        latestVerdict: "MINOR_ISSUES",
        iterations: [makeIteration("MINOR_ISSUES")],
      }),
    };
    render(<AiReviewBlock submissionId="s1" isOpen isCompleted={false} />);

    expect(screen.queryByTestId("student-rerun-button")).toBeNull();
  });

  it("keeps the credit reassurance (not «отправьте снова») if a post-approve re-run finds MAJOR", () => {
    mockQuery = {
      isPending: false,
      isError: false,
      data: makeReview({
        status: "READY",
        latestVerdict: "MAJOR_ISSUES",
        iterations: [makeIteration("MAJOR_ISSUES")],
      }),
    };
    render(<AiReviewBlock submissionId="s1" isOpen={false} isCompleted />);

    const banner = screen.getByTestId("guidance-banner");
    // «остаётся зачтённой … ничего не отбирает» — зачёт сохранён.
    expect(banner).toHaveTextContent(/зачт[её]нн/i);
    expect(banner).not.toHaveTextContent(/отправьте задачу снова/);
    // #976: петля НЕ закрывается на MAJOR — кнопка доступна, баннер на неё указывает.
    expect(screen.getByTestId("student-rerun-button")).toBeInTheDocument();
    expect(banner).toHaveTextContent(/Проверить снова/);
  });

  it("keeps «Проверить снова» after a post-approve re-run FAILED (latestVerdict reset to null)", () => {
    // #976-регрессия: упавшая итерация обнуляет latestVerdict; кнопка должна остаться,
    // иначе студент заперт (ре-сабмит невозможен — задание уже зачтено).
    mockQuery = {
      isPending: false,
      isError: false,
      data: makeReview({
        status: "FAILED",
        latestVerdict: null,
        iterations: [
          makeIteration("MINOR_ISSUES"),
          {
            id: "it2",
            iterationNumber: 2,
            status: "FAILED",
            failureReason: "review.llm.unavailable",
          } as AiReviewDetailDto["iterations"][number],
        ],
      }),
    };
    render(<AiReviewBlock submissionId="s1" isOpen={false} isCompleted />);

    expect(screen.getByTestId("student-rerun-button")).toBeInTheDocument();
  });

  it("hides «Проверить снова» when no iteration has completed yet (QUEUED review)", () => {
    // Зеркалит бэкендовый 409 review.rerun.not_available — кнопка не должна вести к ошибке.
    mockQuery = {
      isPending: false,
      isError: false,
      data: makeReview({ status: "QUEUED", latestVerdict: null, iterations: [] }),
    };
    render(<AiReviewBlock submissionId="s1" isOpen={false} isCompleted />);

    expect(screen.queryByTestId("student-rerun-button")).toBeNull();
  });

  it("disables «Проверить снова» while a re-run iteration is in flight (RUNNING)", () => {
    mockQuery = {
      isPending: false,
      isError: false,
      data: makeReview({
        status: "RUNNING",
        latestVerdict: "MINOR_ISSUES",
        iterations: [makeIteration("MINOR_ISSUES")],
      }),
    };
    render(<AiReviewBlock submissionId="s1" isOpen={false} isCompleted />);

    expect(screen.getByTestId("student-rerun-button")).toBeDisabled();
  });
});
