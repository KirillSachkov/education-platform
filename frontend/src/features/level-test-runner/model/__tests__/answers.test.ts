import { describe, expect, it } from "vitest";
import {
  buildSubmitAnswers,
  countAnswered,
  EMPTY_LEVEL_TEST_ANSWER,
  getAnswerDraft,
  isQuestionAnswered,
  setSingleChoice,
  setTextAnswer,
  toggleMultiChoice,
  type LevelTestAnswersState,
} from "../answers";

const QUESTIONS = [
  { id: "q-single", type: "SINGLE_CHOICE" },
  { id: "q-multi", type: "MULTI_CHOICE" },
  { id: "q-open", type: "OPEN_TEXT" },
  { id: "q-exact", type: "EXACT_TEXT" },
] as const;

describe("answers state", () => {
  it("returns empty draft for unanswered question", () => {
    expect(getAnswerDraft({}, "q-single")).toEqual(EMPTY_LEVEL_TEST_ANSWER);
  });

  it("setSingleChoice replaces previous selection", () => {
    let state: LevelTestAnswersState = {};
    state = setSingleChoice(state, "q-single", "opt-1");
    state = setSingleChoice(state, "q-single", "opt-2");
    expect(getAnswerDraft(state, "q-single").selectedOptionIds).toEqual(["opt-2"]);
  });

  it("toggleMultiChoice adds and removes options immutably", () => {
    const initial: LevelTestAnswersState = {};
    const withFirst = toggleMultiChoice(initial, "q-multi", "opt-1", true);
    const withBoth = toggleMultiChoice(withFirst, "q-multi", "opt-2", true);
    const withoutFirst = toggleMultiChoice(withBoth, "q-multi", "opt-1", false);

    expect(withBoth["q-multi"].selectedOptionIds).toEqual(["opt-1", "opt-2"]);
    expect(withoutFirst["q-multi"].selectedOptionIds).toEqual(["opt-2"]);
    // Иммутабельность — исходные состояния не мутируются
    expect(initial).toEqual({});
    expect(withFirst["q-multi"].selectedOptionIds).toEqual(["opt-1"]);
  });

  it("setTextAnswer stores the free-form text", () => {
    const state = setTextAnswer({}, "q-open", "DI — это про инверсию зависимостей");
    expect(getAnswerDraft(state, "q-open").textAnswer).toBe(
      "DI — это про инверсию зависимостей",
    );
  });
});

describe("isQuestionAnswered / countAnswered", () => {
  it("choice question without selection is unanswered", () => {
    expect(isQuestionAnswered(QUESTIONS[0], {})).toBe(false);
  });

  it("open text with whitespace only is unanswered", () => {
    const state = setTextAnswer({}, "q-open", "   \n ");
    expect(isQuestionAnswered(QUESTIONS[2], state)).toBe(false);
  });

  it("counts only answered questions for the «Отвечено X из Y» counter", () => {
    let state: LevelTestAnswersState = {};
    state = setSingleChoice(state, "q-single", "opt-1");
    state = setTextAnswer(state, "q-open", "ответ");
    expect(countAnswered(QUESTIONS, state)).toBe(2);
  });
});

describe("EXACT_TEXT — рукописный точный ответ (#539)", () => {
  it("typed exact answer counts as answered in the question map", () => {
    const state = setTextAnswer({}, "q-exact", "True False");
    expect(isQuestionAnswered({ id: "q-exact", type: "EXACT_TEXT" }, state)).toBe(true);
  });

  it("typed exact answer reaches the submit payload as textAnswer", () => {
    const state = setTextAnswer({}, "q-exact", " 333 ");
    const payload = buildSubmitAnswers([{ id: "q-exact", type: "EXACT_TEXT" }], state);
    expect(payload).toEqual([{ questionId: "q-exact", textAnswer: "333" }]);
  });

  it("empty exact answer is not submitted", () => {
    const payload = buildSubmitAnswers([{ id: "q-exact", type: "EXACT_TEXT" }], {});
    expect(payload).toEqual([]);
  });
});

describe("buildSubmitAnswers", () => {
  it("maps choice answers to selectedOptionIds and trims open text", () => {
    let state: LevelTestAnswersState = {};
    state = setSingleChoice(state, "q-single", "opt-1");
    state = toggleMultiChoice(state, "q-multi", "opt-a", true);
    state = toggleMultiChoice(state, "q-multi", "opt-b", true);
    state = setTextAnswer(state, "q-open", "  развёрнутый ответ  ");

    expect(buildSubmitAnswers(QUESTIONS, state)).toEqual([
      { questionId: "q-single", selectedOptionIds: ["opt-1"] },
      { questionId: "q-multi", selectedOptionIds: ["opt-a", "opt-b"] },
      { questionId: "q-open", textAnswer: "развёрнутый ответ" },
    ]);
  });

  it("omits unanswered questions from the payload entirely", () => {
    const state = setSingleChoice({}, "q-single", "opt-1");
    const payload = buildSubmitAnswers(QUESTIONS, state);
    expect(payload).toHaveLength(1);
    expect(payload[0].questionId).toBe("q-single");
  });

  it("omits open text answer that is whitespace only", () => {
    const state = setTextAnswer({}, "q-open", "   ");
    expect(buildSubmitAnswers(QUESTIONS, state)).toEqual([]);
  });
});
