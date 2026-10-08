import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { LevelTestLandingHero } from "../level-test-landing-hero";

const TEST = {
  id: "test-123",
  title: "Level test",
  totalQuestions: 1,
  sections: [],
  questions: [
    {
      id: "question-1",
      text: "Question",
      type: "SINGLE_CHOICE" as const,
      section: null,
      difficulty: null,
      options: [{ id: "option-1", text: "Answer" }],
    },
  ],
};

describe("LevelTestLandingHero", () => {
  it("offers anonymous visitors the test instead of login", () => {
    render(
      <LevelTestLandingHero
        test={TEST}
        lastAttemptId={null}
        latestResult={null}
        hasDraft={false}
        onStart={vi.fn()}
      />,
    );

    expect(screen.getAllByRole("button", { name: /Начать тест/ })).toHaveLength(2);
    expect(screen.queryAllByRole("button", { name: /Войти и начать тест/ })).toHaveLength(0);
  });
});
