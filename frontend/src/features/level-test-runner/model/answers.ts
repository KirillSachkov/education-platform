import type { LevelTestQuestionDto, SubmitLevelTestAnswerItem } from "@/entities/level-test";

/**
 * Чистая логика черновика ответов level-test'а: иммутабельные апдейты по
 * questionId + маппинг в submit-payload. Вынесена из UI ради unit-тестов и
 * React Compiler (всегда новые ссылки, никаких мутаций in place). Issue #481.
 */

export interface LevelTestAnswerDraft {
  selectedOptionIds: string[];
  textAnswer: string;
}

export type LevelTestAnswersState = Record<string, LevelTestAnswerDraft>;

export const EMPTY_LEVEL_TEST_ANSWER: LevelTestAnswerDraft = {
  selectedOptionIds: [],
  textAnswer: "",
};

export function getAnswerDraft(
  state: LevelTestAnswersState,
  questionId: string,
): LevelTestAnswerDraft {
  return state[questionId] ?? EMPTY_LEVEL_TEST_ANSWER;
}

/** SINGLE_CHOICE: выбор варианта заменяет предыдущий. */
export function setSingleChoice(
  state: LevelTestAnswersState,
  questionId: string,
  optionId: string,
): LevelTestAnswersState {
  return {
    ...state,
    [questionId]: { ...EMPTY_LEVEL_TEST_ANSWER, selectedOptionIds: [optionId] },
  };
}

/** MULTI_CHOICE: добавляет/убирает вариант из множества выбранных. */
export function toggleMultiChoice(
  state: LevelTestAnswersState,
  questionId: string,
  optionId: string,
  checked: boolean,
): LevelTestAnswersState {
  const current = getAnswerDraft(state, questionId);
  const selectedOptionIds = checked
    ? [...current.selectedOptionIds, optionId]
    : current.selectedOptionIds.filter((id) => id !== optionId);
  return { ...state, [questionId]: { ...current, selectedOptionIds } };
}

/** OPEN_TEXT: свободный ответ своими словами. */
export function setTextAnswer(
  state: LevelTestAnswersState,
  questionId: string,
  textAnswer: string,
): LevelTestAnswersState {
  return {
    ...state,
    [questionId]: { ...EMPTY_LEVEL_TEST_ANSWER, textAnswer },
  };
}

/** Текстовый ввод: и развёрнутый ответ, и рукописный точный (#539). */
function isTextQuestion(type: string): boolean {
  return type === "OPEN_TEXT" || type === "EXACT_TEXT";
}

/** Вопрос считается отвеченным: choice — выбран ≥1 вариант, текстовый — непустой текст. */
export function isQuestionAnswered(
  question: Pick<LevelTestQuestionDto, "id" | "type">,
  state: LevelTestAnswersState,
): boolean {
  const draft = getAnswerDraft(state, question.id);
  if (isTextQuestion(question.type)) {
    return draft.textAnswer.trim().length > 0;
  }
  return draft.selectedOptionIds.length > 0;
}

/** Счётчик «Отвечено X из Y» перед сабмитом — незаполненные ответы разрешены. */
export function countAnswered(
  questions: ReadonlyArray<Pick<LevelTestQuestionDto, "id" | "type">>,
  state: LevelTestAnswersState,
): number {
  return questions.filter((question) => isQuestionAnswered(question, state)).length;
}

/**
 * Маппинг черновика в payload `POST /progress/level-test/attempts/`.
 * Неотвеченные вопросы НЕ отправляются (контракт бэкенда: choice без ответа →
 * неверно, open_text — не грейдится). Текст триммится.
 */
export function buildSubmitAnswers(
  questions: ReadonlyArray<Pick<LevelTestQuestionDto, "id" | "type">>,
  state: LevelTestAnswersState,
): SubmitLevelTestAnswerItem[] {
  return questions
    .filter((question) => isQuestionAnswered(question, state))
    .map((question) => {
      const draft = getAnswerDraft(state, question.id);
      if (isTextQuestion(question.type)) {
        return { questionId: question.id, textAnswer: draft.textAnswer.trim() };
      }
      return { questionId: question.id, selectedOptionIds: draft.selectedOptionIds };
    });
}
