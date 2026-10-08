import { describe, expect, it } from "vitest";
import { getTrainerTopicStudyStatus } from "../topic-status";

describe("trainer topic study status", () => {
  it("keeps unstudied weak topics neutral", () => {
    expect(
      getTrainerTopicStudyStatus({
        masteryPercent: 0,
        isWeak: true,
        answersCount: 0,
        studiedCount: 0,
        mistakesCount: 0,
      }),
    ).toEqual({
      kind: "not-started",
      label: "Не изучена",
      hasActivity: false,
    });
  });

  it("marks attempted weak topics as needing work", () => {
    expect(
      getTrainerTopicStudyStatus({
        masteryPercent: 35,
        isWeak: true,
        answersCount: 4,
        mistakesCount: 2,
      }).kind,
    ).toBe("needs-work");
  });

  it("uses mistake pressure even without an explicit weak flag", () => {
    expect(
      getTrainerTopicStudyStatus({
        masteryPercent: 72,
        isWeak: false,
        answersCount: 6,
        mistakesCount: 3,
      }).label,
    ).toBe("Нужно подтянуть");
  });

  it("keeps high mastery with pending mistakes in review state", () => {
    expect(
      getTrainerTopicStudyStatus({
        masteryPercent: 92,
        isWeak: false,
        answersCount: 20,
        mistakesCount: 1,
      }).kind,
    ).toBe("review-errors");
  });

  it("marks high topic COVERAGE without mistakes as strong (#664)", () => {
    expect(
      getTrainerTopicStudyStatus({
        masteryPercent: 90,
        coveragePercent: 90,
        isWeak: false,
        answersCount: 12,
        mistakesCount: 0,
      }).label,
    ).toBe("Хорошо изучена");
  });

  it("does NOT mark high EWMA mastery with low coverage as strong (#664)", () => {
    // Регрессия #664: 1 верный ответ в мок-собесе раздувал EWMA-mastery до 100, и тема
    // показывалась «Хорошо изучена». Теперь «хорошо изучена» = высокое ПОКРЫТИЕ темы, поэтому
    // 1 из 12 (≈8% покрытия) остаётся «В процессе».
    expect(
      getTrainerTopicStudyStatus({
        masteryPercent: 100,
        coveragePercent: 8,
        isWeak: false,
        answersCount: 1,
        mistakesCount: 0,
      }),
    ).toEqual({ kind: "in-progress", label: "В процессе", hasActivity: true });
  });
});
