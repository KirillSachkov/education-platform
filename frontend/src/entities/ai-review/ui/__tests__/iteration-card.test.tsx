import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { IterationCard } from "../iteration-card";
import type { AiReviewIterationDto } from "@/entities/ai-review";

function makeIteration(overrides: Partial<AiReviewIterationDto> = {}): AiReviewIterationDto {
  return {
    id: "i1",
    iterationNumber: 1,
    commitSha: "deadbeef",
    status: "COMPLETED",
    verdict: "MINOR_ISSUES",
    summary: "Реализация в целом ок, поправь нейминг.",
    inlineCommentsCount: 3,
    gitHubReviewId: 42,
    modelUsed: "openai/gpt-4.1-mini",
    inputTokens: 1000,
    outputTokens: 200,
    startedAt: "2026-05-10T12:00:00Z",
    completedAt: "2026-05-10T12:01:00Z",
    failureReason: null,
    ...overrides,
  };
}

describe("IterationCard", () => {
  it("renders verdict badge with Russian label", () => {
    render(
      <IterationCard
        iteration={makeIteration({ verdict: "LOOKS_GOOD" })}
        pullRequestUrl="https://github.com/owner/repo/pull/1"
      />,
    );
    expect(screen.getByTestId("iteration-verdict")).toHaveTextContent("Принято");
  });

  it("renders MAJOR_ISSUES verdict", () => {
    render(
      <IterationCard
        iteration={makeIteration({ verdict: "MAJOR_ISSUES" })}
        pullRequestUrl="https://github.com/owner/repo/pull/1"
      />,
    );
    expect(screen.getByTestId("iteration-verdict")).toHaveTextContent("Серьёзные замечания");
  });

  it("shows summary text", () => {
    render(
      <IterationCard
        iteration={makeIteration()}
        pullRequestUrl="https://github.com/owner/repo/pull/1"
      />,
    );
    expect(screen.getByText(/Реализация в целом ок/)).toBeInTheDocument();
  });

  it("shows inline comments count", () => {
    render(
      <IterationCard
        iteration={makeIteration({ inlineCommentsCount: 5 })}
        pullRequestUrl="https://github.com/owner/repo/pull/1"
      />,
    );
    expect(screen.getByText(/5 inline-комментариев/)).toBeInTheDocument();
  });

  it("shows fallback when no inline comments", () => {
    render(
      <IterationCard
        iteration={makeIteration({ inlineCommentsCount: 0 })}
        pullRequestUrl="https://github.com/owner/repo/pull/1"
      />,
    );
    expect(screen.getByText(/Inline-комментариев нет/)).toBeInTheDocument();
  });

  it("renders PR review link with anchor", () => {
    render(
      <IterationCard
        iteration={makeIteration({ gitHubReviewId: 99 })}
        pullRequestUrl="https://github.com/owner/repo/pull/1"
      />,
    );
    const link = screen.getByTestId("iteration-pr-link") as HTMLAnchorElement;
    expect(link.href).toBe("https://github.com/owner/repo/pull/1#pullrequestreview-99");
  });

  it("hides PR link when GitHub review wasn't posted", () => {
    render(
      <IterationCard
        iteration={makeIteration({ gitHubReviewId: null })}
        pullRequestUrl="https://github.com/owner/repo/pull/1"
      />,
    );
    expect(screen.queryByTestId("iteration-pr-link")).toBeNull();
  });

  it("shows failure copy for known error code", () => {
    render(
      <IterationCard
        iteration={makeIteration({
          status: "FAILED",
          verdict: null,
          failureReason: "review.diff.too_large",
        })}
        pullRequestUrl="https://github.com/owner/repo/pull/1"
      />,
    );
    expect(
      screen.getByText(/PR большой — авто-проверка пропущена/),
    ).toBeInTheDocument();
  });

  it("shows generic copy for unknown failure code", () => {
    render(
      <IterationCard
        iteration={makeIteration({
          status: "FAILED",
          verdict: null,
          failureReason: "review.unexpected.code",
        })}
        pullRequestUrl="https://github.com/owner/repo/pull/1"
      />,
    );
    expect(screen.getByText(/Неизвестная ошибка AI-проверки/)).toBeInTheDocument();
  });
});
