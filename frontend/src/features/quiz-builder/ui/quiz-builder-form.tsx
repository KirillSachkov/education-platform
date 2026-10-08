"use client";

import {
  QUIZ_TITLE_MAX_LENGTH,
  type QuizAccessType,
  type QuizAuthorDto,
} from "@/entities/quiz";
import { AccessTypeSelector } from "@/shared/ui/components/access-type-selector";
import { DeleteConfirmDialog } from "@/shared/ui/components/delete-confirm-dialog";
import { Icons } from "@/shared/ui/icons";
import { Button } from "@/shared/ui/kit/button";
import { Input } from "@/shared/ui/kit/input";
import { Label } from "@/shared/ui/kit/label";
import { useState } from "react";
import {
  flattenQuizBuilderIssues,
  quizBuilderSchema,
  toQuestionDrafts,
  toQuestionRequest,
  type QuizQuestionDraft,
} from "../model/schemas";
import { useDeleteQuiz } from "../model/use-delete-quiz";
import { usePublishQuiz } from "../model/use-publish-quiz";
import { useSaveQuiz } from "../model/use-save-quiz";
import { QuizQuestionsListEditor } from "./quiz-questions-list-editor";

interface QuizBuilderFormProps {
  quiz: QuizAuthorDto;
  /**
   * Привязан ли квиз хотя бы к одному курсу (course_quizzes) — меняет подсказку
   * ENROLLED в селекторе доступа: «полная запись» vs «платный план автора».
   */
  hasCourseBinding?: boolean;
  /** Вызывается после успешного удаления (свернуть редактор в библиотеке). */
  onDeleted?: () => void;
}

/**
 * Редактор standalone-квиза в библиотеке (#494): название, проходной балл,
 * уровень доступа, вопросы (3 типа, стрелки порядка). Квиз создаётся отдельным
 * диалогом (POST title) — форма работает только с существующим. «Сохранить» —
 * PUT replace; «Опубликовать» — для DRAFT'а с вопросами; «Удалить» — hard-delete
 * с каскадом по материалам/модулям/подборкам.
 */
export function QuizBuilderForm({ quiz, hasCourseBinding = false, onDeleted }: QuizBuilderFormProps) {
  const [title, setTitle] = useState(quiz.title);
  const [passingScore, setPassingScore] = useState(String(quiz.passingScorePercent));
  const [accessType, setAccessType] = useState<QuizAccessType>(quiz.accessType);
  const [questions, setQuestions] = useState<QuizQuestionDraft[]>(() => toQuestionDrafts(quiz));
  const [errors, setErrors] = useState<Record<string, string>>({});

  const saveMutation = useSaveQuiz();
  const publishMutation = usePublishQuiz();
  const deleteMutation = useDeleteQuiz();
  const isBusy = saveMutation.isPending || publishMutation.isPending || deleteMutation.isPending;

  const handleSave = () => {
    const parsed = quizBuilderSchema.safeParse({
      title,
      passingScorePercent: passingScore.trim() === "" ? Number.NaN : Number(passingScore),
      questions,
    });
    if (!parsed.success) {
      setErrors(flattenQuizBuilderIssues(parsed.error));
      return;
    }
    setErrors({});
    saveMutation.mutate({
      quizId: quiz.id,
      title: parsed.data.title,
      passingScorePercent: parsed.data.passingScorePercent,
      accessType,
      questions: parsed.data.questions.map(toQuestionRequest),
    });
  };

  return (
    <div className="space-y-4">
      <div className="grid gap-4 md:grid-cols-[minmax(0,1fr)_180px]">
        <div className="space-y-1.5">
          <Label htmlFor={`quiz-title-${quiz.id}`}>Название теста</Label>
          <Input
            id={`quiz-title-${quiz.id}`}
            value={title}
            onChange={(event) => setTitle(event.target.value)}
            maxLength={QUIZ_TITLE_MAX_LENGTH}
            placeholder="Например: Проверь себя по теме урока"
            disabled={isBusy}
          />
          {errors.title && <p className="text-xs text-destructive">{errors.title}</p>}
        </div>
        <div className="space-y-1.5">
          <Label htmlFor={`quiz-passing-score-${quiz.id}`}>Проходной балл, %</Label>
          <Input
            id={`quiz-passing-score-${quiz.id}`}
            type="number"
            min={0}
            max={100}
            value={passingScore}
            onChange={(event) => setPassingScore(event.target.value)}
            disabled={isBusy}
          />
          {errors.passingScorePercent && (
            <p className="text-xs text-destructive">{errors.passingScorePercent}</p>
          )}
        </div>
      </div>

      <div className="space-y-1.5">
        <Label>Уровень доступа</Label>
        <AccessTypeSelector
          value={accessType}
          onChange={(value) => setAccessType(value as QuizAccessType)}
          hasCourseBinding={hasCourseBinding}
        />
      </div>

      <QuizQuestionsListEditor
        questions={questions}
        onQuestionsChange={setQuestions}
        errors={errors}
        disabled={isBusy}
      />

      <div className="flex flex-wrap items-center gap-2 border-t border-border/60 pt-4">
        <Button type="button" onClick={handleSave} disabled={isBusy}>
          {saveMutation.isPending ? (
            <Icons.loading className="size-4 animate-spin" />
          ) : (
            <Icons.save className="size-4" />
          )}
          Сохранить
        </Button>

        {quiz.status === "DRAFT" && (
          <Button
            type="button"
            variant="outline"
            onClick={() => publishMutation.mutate(quiz.id)}
            disabled={isBusy || quiz.questions.length === 0}
            title={
              quiz.questions.length === 0
                ? "Сохраните хотя бы один вопрос, чтобы опубликовать"
                : undefined
            }
          >
            {publishMutation.isPending ? (
              <Icons.loading className="size-4 animate-spin" />
            ) : (
              <Icons.send className="size-4" />
            )}
            Опубликовать
          </Button>
        )}

        <div className="ml-auto">
          <DeleteConfirmDialog
            title="Удалить тест?"
            description={
              <>
                Тест <span className="font-medium">«{quiz.title}»</span> будет удалён безвозвратно
                и исчезнет из всех материалов, модулей и подборок, где он используется. Попытки
                студентов перестанут отображаться.
              </>
            }
            onConfirm={() => deleteMutation.mutate(quiz.id, { onSuccess: () => onDeleted?.() })}
            isPending={deleteMutation.isPending}
            trigger={
              <Button
                type="button"
                variant="outline"
                className="text-destructive hover:text-destructive"
                disabled={isBusy}
              >
                <Icons.delete className="size-4" />
                Удалить тест
              </Button>
            }
          />
        </div>
      </div>
    </div>
  );
}
