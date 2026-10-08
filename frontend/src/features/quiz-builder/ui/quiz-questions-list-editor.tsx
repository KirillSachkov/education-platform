"use client";

import { QUIZ_MAX_QUESTIONS } from "@/entities/quiz";
import { Icons } from "@/shared/ui/icons";
import { Button } from "@/shared/ui/kit/button";
import { createQuizQuestionDraft, type QuizQuestionDraft } from "../model/schemas";
import { QuizQuestionEditor, type QuizQuestionLevelTestProps } from "./quiz-question-editor";

interface QuizQuestionsListEditorProps {
  questions: QuizQuestionDraft[];
  onQuestionsChange: (questions: QuizQuestionDraft[]) => void;
  /** Ошибки валидации всей формы, ключи вида `questions.{index}.text` / `questions`. */
  errors: Record<string, string>;
  disabled: boolean;
  /** Level-test режим: показывает per-question селекты «Секция»/«Сложность» (#487). */
  levelTest?: QuizQuestionLevelTestProps;
}

/**
 * Список вопросов билдера: добавление / удаление / стрелки порядка поверх
 * `QuizQuestionEditor`. Общий для material-билдера (`QuizBuilderForm`) и
 * редактора level-test'а — одна реализация list-менеджмента. Issue #487.
 */
export function QuizQuestionsListEditor({
  questions,
  onQuestionsChange,
  errors,
  disabled,
  levelTest,
}: QuizQuestionsListEditorProps) {
  const updateQuestion = (index: number, updated: QuizQuestionDraft) => {
    onQuestionsChange(questions.map((question, i) => (i === index ? updated : question)));
  };

  const removeQuestion = (index: number) => {
    onQuestionsChange(questions.filter((_, i) => i !== index));
  };

  const moveQuestion = (index: number, direction: -1 | 1) => {
    const target = index + direction;
    if (target < 0 || target >= questions.length) return;
    const next = [...questions];
    [next[index], next[target]] = [next[target], next[index]];
    onQuestionsChange(next);
  };

  const addQuestion = () => {
    onQuestionsChange([...questions, createQuizQuestionDraft()]);
  };

  return (
    <div className="space-y-3">
      {questions.map((question, index) => (
        <QuizQuestionEditor
          key={question.id}
          question={question}
          index={index}
          totalCount={questions.length}
          errors={errors}
          onChange={(updated) => updateQuestion(index, updated)}
          onRemove={() => removeQuestion(index)}
          onMoveUp={() => moveQuestion(index, -1)}
          onMoveDown={() => moveQuestion(index, 1)}
          disabled={disabled}
          levelTest={levelTest}
        />
      ))}
      {errors.questions && <p className="text-xs text-destructive">{errors.questions}</p>}
      <Button
        type="button"
        variant="outline"
        size="sm"
        onClick={addQuestion}
        disabled={disabled || questions.length >= QUIZ_MAX_QUESTIONS}
      >
        <Icons.add className="size-3.5" />
        Добавить вопрос
      </Button>
    </div>
  );
}
