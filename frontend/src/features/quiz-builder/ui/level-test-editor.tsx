"use client";

import { coursesApi, coursesQueryOptions, type CourseSummaryDto } from "@/entities/course";
import {
  QUIZ_SECTION_KEY_MAX_LENGTH,
  QUIZ_TITLE_MAX_LENGTH,
  type LevelTestConfigDto,
  type QuizAuthorDto,
} from "@/entities/quiz";
import { routes } from "@/shared/config/routes";
import { pluralize } from "@/shared/lib/pluralize";
import { StatusBadge } from "@/shared/ui/components/status-badge";
import { Icons } from "@/shared/ui/icons";
import { Button } from "@/shared/ui/kit/button";
import { Input } from "@/shared/ui/kit/input";
import { Label } from "@/shared/ui/kit/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/shared/ui/kit/select";
import { Switch } from "@/shared/ui/kit/switch";
import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { useState } from "react";
import { toast } from "sonner";
import { DEVELOPER_LEVEL_LABELS } from "@/entities/level-test";
import {
  LEVEL_TEST_BASE_MIN_PERCENT,
  LEVEL_TEST_DEFAULT_THRESHOLDS,
  LEVEL_TEST_EDITABLE_LEVELS,
  levelTestBuilderSchema,
  levelTestSectionKeySchema,
  toLevelTestUpdateRequest,
  type LevelTestEditableLevel,
} from "../model/level-test-schemas";
import {
  flattenQuizBuilderIssues,
  toQuestionDrafts,
  type QuizQuestionDraft,
} from "../model/schemas";
import { usePublishLevelTest } from "../model/use-publish-level-test";
import { useSaveLevelTest } from "../model/use-save-level-test";
import { QuizQuestionsListEditor } from "./quiz-questions-list-editor";

/** Сентинел «не выбрано» для Radix Select (пустые value у SelectItem запрещены). */
const NONE_VALUE = "__none__";

/** Строка секции в редакторе: key immutable после добавления, weight — текст инпута. */
interface SectionRowState {
  key: string;
  title: string;
  weight: string;
  recommendedCourseId: string | null;
}

function toSectionRows(config: LevelTestConfigDto | null): SectionRowState[] {
  return (config?.sections ?? []).map((section) => ({
    key: section.key,
    title: section.title,
    weight: String(section.weight),
    recommendedCourseId: section.recommendedCourseId,
  }));
}

/** Стейт инпутов порогов: значение из конфига или дефолт шкалы, строкой. */
function toThresholdInputs(
  config: LevelTestConfigDto | null,
): Record<LevelTestEditableLevel, string> {
  const inputs = {} as Record<LevelTestEditableLevel, string>;
  for (const level of LEVEL_TEST_EDITABLE_LEVELS) {
    const fromConfig = config?.levelThresholds.find((t) => t.level === level)?.minPercent;
    inputs[level] = String(fromConfig ?? LEVEL_TEST_DEFAULT_THRESHOLDS[level]);
  }
  return inputs;
}

function toNumberOrNaN(raw: string): number {
  return raw.trim() === "" ? Number.NaN : Number(raw);
}

interface LevelTestEditorProps {
  quiz: QuizAuthorDto;
}

/**
 * Авторский редактор level-test'а (#487): название + статус, секции конфига
 * (key/title/weight/рекомендуемый курс), пороги уровней, курс по умолчанию и
 * вопросы с per-question секцией/сложностью. «Сохранить» — PUT replace целиком.
 */
export function LevelTestEditor({ quiz }: LevelTestEditorProps) {
  const [title, setTitle] = useState(quiz.title);
  const [thresholds, setThresholds] = useState<Record<LevelTestEditableLevel, string>>(() =>
    toThresholdInputs(quiz.levelTestConfig),
  );
  const [sections, setSections] = useState<SectionRowState[]>(() =>
    toSectionRows(quiz.levelTestConfig),
  );
  const [fallbackCourseId, setFallbackCourseId] = useState<string | null>(
    quiz.levelTestConfig?.fallbackCourseId ?? null,
  );
  const [questions, setQuestions] = useState<QuizQuestionDraft[]>(() => toQuestionDrafts(quiz));
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [showWeights, setShowWeights] = useState(false);
  const [newSectionKey, setNewSectionKey] = useState("");
  const [newSectionTitle, setNewSectionTitle] = useState("");
  const [newSectionError, setNewSectionError] = useState<string | null>(null);

  const coursesQuery = useQuery({
    queryKey: [coursesQueryOptions.baseKey, "my", "level-test-editor"] as const,
    queryFn: ({ signal }) => coursesApi.getMyCourses({ limit: 100 }, { signal }),
    select: (data) => data.result?.items ?? [],
    staleTime: 60_000,
  });
  const courses = coursesQuery.data ?? [];

  const saveMutation = useSaveLevelTest();
  const publishMutation = usePublishLevelTest();
  const isBusy = saveMutation.isPending || publishMutation.isPending;

  const handleSave = () => {
    const parsed = levelTestBuilderSchema.safeParse({
      title,
      thresholds: Object.fromEntries(
        LEVEL_TEST_EDITABLE_LEVELS.map((level) => [level, toNumberOrNaN(thresholds[level])]),
      ),
      sections: sections.map((section) => ({
        key: section.key,
        title: section.title,
        weight: toNumberOrNaN(section.weight),
        recommendedCourseId: section.recommendedCourseId,
      })),
      fallbackCourseId,
      questions,
    });
    if (!parsed.success) {
      setErrors(flattenQuizBuilderIssues(parsed.error));
      toast.error("Проверьте подсвеченные поля");
      return;
    }
    setErrors({});
    saveMutation.mutate({
      quizId: quiz.id,
      request: toLevelTestUpdateRequest(parsed.data, quiz.passingScorePercent),
    });
  };

  const handleAddSection = () => {
    const keyParsed = levelTestSectionKeySchema.safeParse(newSectionKey);
    if (!keyParsed.success) {
      setNewSectionError(keyParsed.error.issues[0]?.message ?? "Некорректный ключ");
      return;
    }
    if (sections.some((section) => section.key === keyParsed.data)) {
      setNewSectionError("Секция с таким ключом уже есть");
      return;
    }
    if (newSectionTitle.trim().length === 0) {
      setNewSectionError("Введите название секции");
      return;
    }
    setNewSectionError(null);
    setSections((prev) => [
      ...prev,
      {
        key: keyParsed.data,
        title: newSectionTitle.trim(),
        weight: "1",
        recommendedCourseId: null,
      },
    ]);
    setNewSectionKey("");
    setNewSectionTitle("");
  };

  const handleRemoveSection = (key: string) => {
    const usedBy = questions.filter((question) => question.section === key).length;
    if (usedBy > 0) {
      toast.error(
        `Сначала убери секцию у ${usedBy} ${pluralize(usedBy, "вопроса", "вопросов", "вопросов")}`,
      );
      return;
    }
    setSections((prev) => prev.filter((section) => section.key !== key));
  };

  const updateSection = (key: string, patch: Partial<Omit<SectionRowState, "key">>) => {
    setSections((prev) =>
      prev.map((section) => (section.key === key ? { ...section, ...patch } : section)),
    );
  };

  const selectSections = sections.map(({ key, title: sectionTitle }) => ({
    key,
    title: sectionTitle,
  }));

  return (
    <div className="space-y-8">
      {/* ===== Header: название + статус + действия ===== */}
      <div className="space-y-3">
        <div className="flex flex-col gap-3 sm:flex-row sm:items-end">
          <div className="min-w-0 flex-1 space-y-1.5">
            <div className="flex items-center gap-2">
              <Label htmlFor="level-test-title">Название теста</Label>
              <StatusBadge status={quiz.status} />
            </div>
            <Input
              id="level-test-title"
              value={title}
              onChange={(event) => setTitle(event.target.value)}
              maxLength={QUIZ_TITLE_MAX_LENGTH}
              disabled={isBusy}
            />
            {errors.title && <p className="text-xs text-destructive">{errors.title}</p>}
          </div>
          <div className="flex flex-wrap items-center gap-2">
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
            <Button type="button" variant="ghost" asChild>
              <Link href={routes.levelTest} target="_blank" rel="noopener">
                <Icons.externalLink className="size-4" />
                Страница теста
              </Link>
            </Button>
          </div>
        </div>

        {quiz.status === "PUBLISHED" && (
          <p className="flex items-start gap-2 rounded-lg border border-amber-500/30 bg-amber-500/10 px-3 py-2 text-sm text-amber-600 dark:text-amber-400">
            <Icons.warning className="mt-0.5 size-4 shrink-0" />
            Тест опубликован — изменения станут видны студентам сразу после сохранения.
          </p>
        )}
      </div>

      {/* ===== Секции ===== */}
      <section className="space-y-3">
        <div className="flex flex-wrap items-center gap-3">
          <h2 className="text-lg font-semibold">Секции</h2>
          <label className="ml-auto flex items-center gap-2 text-xs text-muted-foreground">
            Веса секций
            <Switch checked={showWeights} onCheckedChange={setShowWeights} disabled={isBusy} />
          </label>
        </div>
        <p className="text-sm text-muted-foreground">
          Тематические блоки вопросов. По слабым секциям студент получит рекомендуемый курс на
          странице результата. Ключ секции фиксируется при добавлении — на него ссылаются вопросы.
        </p>

        {sections.length === 0 && (
          <p className="rounded-lg border border-dashed border-border/70 bg-card/30 p-4 text-sm text-muted-foreground">
            Секций пока нет — добавьте первую, чтобы группировать вопросы и давать рекомендации по
            курсам.
          </p>
        )}

        <div className="space-y-2">
          {sections.map((section, index) => (
            <div
              key={section.key}
              className="space-y-2 rounded-xl border border-border/60 bg-card/50 p-3"
            >
              <div className="flex items-center gap-2">
                <code className="rounded bg-muted px-1.5 py-0.5 text-xs text-muted-foreground">
                  {section.key}
                </code>
                <Button
                  type="button"
                  variant="ghost"
                  size="icon"
                  className="ml-auto size-7 text-muted-foreground hover:text-destructive"
                  onClick={() => handleRemoveSection(section.key)}
                  disabled={isBusy}
                  aria-label={`Удалить секцию ${section.title}`}
                >
                  <Icons.delete className="size-4" />
                </Button>
              </div>
              <div className="grid gap-2 sm:grid-cols-2">
                <div className="space-y-1">
                  <Label htmlFor={`section-title-${section.key}`} className="text-xs">
                    Название
                  </Label>
                  <Input
                    id={`section-title-${section.key}`}
                    value={section.title}
                    onChange={(event) => updateSection(section.key, { title: event.target.value })}
                    maxLength={QUIZ_TITLE_MAX_LENGTH}
                    disabled={isBusy}
                    className="h-9"
                  />
                  {errors[`sections.${index}.title`] && (
                    <p className="text-xs text-destructive">{errors[`sections.${index}.title`]}</p>
                  )}
                </div>
                <div className="space-y-1">
                  <Label htmlFor={`section-course-${section.key}`} className="text-xs">
                    Рекомендуемый курс
                  </Label>
                  <CourseSelect
                    id={`section-course-${section.key}`}
                    value={section.recommendedCourseId}
                    onChange={(courseId) =>
                      updateSection(section.key, { recommendedCourseId: courseId })
                    }
                    courses={courses}
                    disabled={isBusy || coursesQuery.isLoading}
                  />
                </div>
              </div>
              {showWeights && (
                <div className="space-y-1">
                  <Label htmlFor={`section-weight-${section.key}`} className="text-xs">
                    Вес в скоринге
                  </Label>
                  <Input
                    id={`section-weight-${section.key}`}
                    type="number"
                    min={0.1}
                    step={0.1}
                    inputMode="decimal"
                    value={section.weight}
                    onChange={(event) => updateSection(section.key, { weight: event.target.value })}
                    disabled={isBusy}
                    className="h-9 max-w-32"
                  />
                  {errors[`sections.${index}.weight`] && (
                    <p className="text-xs text-destructive">{errors[`sections.${index}.weight`]}</p>
                  )}
                </div>
              )}
            </div>
          ))}
        </div>
        {errors.sections && <p className="text-xs text-destructive">{errors.sections}</p>}

        {/* Добавление секции: key фиксируется при создании строки */}
        <div className="space-y-2 rounded-xl border border-dashed border-border/70 p-3">
          <div className="grid gap-2 sm:grid-cols-[200px_minmax(0,1fr)_auto]">
            <div className="space-y-1">
              <Label htmlFor="new-section-key" className="text-xs">
                Ключ (kebab-case)
              </Label>
              <Input
                id="new-section-key"
                value={newSectionKey}
                onChange={(event) => setNewSectionKey(event.target.value)}
                maxLength={QUIZ_SECTION_KEY_MAX_LENGTH}
                placeholder="csharp-basics"
                disabled={isBusy}
                className="h-9"
              />
            </div>
            <div className="space-y-1">
              <Label htmlFor="new-section-title" className="text-xs">
                Название
              </Label>
              <Input
                id="new-section-title"
                value={newSectionTitle}
                onChange={(event) => setNewSectionTitle(event.target.value)}
                maxLength={QUIZ_TITLE_MAX_LENGTH}
                placeholder="Основы C#"
                disabled={isBusy}
                className="h-9"
              />
            </div>
            <Button
              type="button"
              variant="outline"
              size="sm"
              onClick={handleAddSection}
              disabled={isBusy}
              className="self-end"
            >
              <Icons.add className="size-3.5" />
              Добавить секцию
            </Button>
          </div>
          {newSectionError && <p className="text-xs text-destructive">{newSectionError}</p>}
        </div>

        <div className="max-w-md space-y-1.5">
          <Label htmlFor="fallback-course">Курс по умолчанию</Label>
          <CourseSelect
            id="fallback-course"
            value={fallbackCourseId}
            onChange={setFallbackCourseId}
            courses={courses}
            disabled={isBusy || coursesQuery.isLoading}
          />
          <p className="text-xs text-muted-foreground">
            Рекомендуется на странице результата, если для слабой секции курс не настроен.
          </p>
        </div>
      </section>

      {/* ===== Пороги уровней ===== */}
      <section className="space-y-3">
        <h2 className="text-lg font-semibold">Пороги уровней</h2>
        <p className="text-sm text-muted-foreground">
          Минимальный общий процент для присвоения уровня. «Новичок» — базовый уровень,
          начинается с 0%; пороги выше должны строго расти.
        </p>
        <div className="grid max-w-3xl gap-3 sm:grid-cols-3 lg:grid-cols-6">
          <div className="space-y-1.5">
            <Label htmlFor="threshold-pre-junior">{DEVELOPER_LEVEL_LABELS.PRE_JUNIOR}, от %</Label>
            <Input
              id="threshold-pre-junior"
              value={LEVEL_TEST_BASE_MIN_PERCENT}
              disabled
              readOnly
              aria-label="Порог базового уровня фиксирован: 0%"
            />
          </div>
          {LEVEL_TEST_EDITABLE_LEVELS.map((level) => (
            <div key={level} className="space-y-1.5">
              <Label htmlFor={`threshold-${level}`}>{DEVELOPER_LEVEL_LABELS[level]}, от %</Label>
              <Input
                id={`threshold-${level}`}
                type="number"
                min={1}
                max={100}
                inputMode="numeric"
                value={thresholds[level]}
                onChange={(event) =>
                  setThresholds((prev) => ({ ...prev, [level]: event.target.value }))
                }
                disabled={isBusy}
              />
              {errors[`thresholds.${level}`] && (
                <p className="text-xs text-destructive">{errors[`thresholds.${level}`]}</p>
              )}
            </div>
          ))}
        </div>
      </section>

      {/* ===== Вопросы ===== */}
      <section className="space-y-3">
        <h2 className="text-lg font-semibold">Вопросы</h2>
        <p className="text-sm text-muted-foreground">
          У каждого вопроса — секция (для рекомендаций по курсам) и сложность
          (Junior/Middle/Senior).
        </p>
        <QuizQuestionsListEditor
          questions={questions}
          onQuestionsChange={setQuestions}
          errors={errors}
          disabled={isBusy}
          levelTest={{ sections: selectSections }}
        />
      </section>
    </div>
  );
}

function CourseSelect({
  id,
  value,
  onChange,
  courses,
  disabled,
}: {
  id: string;
  value: string | null;
  onChange: (courseId: string | null) => void;
  courses: CourseSummaryDto[];
  disabled: boolean;
}) {
  return (
    <Select
      value={value ?? NONE_VALUE}
      onValueChange={(selected) => onChange(selected === NONE_VALUE ? null : selected)}
      disabled={disabled}
    >
      <SelectTrigger id={id} className="w-full">
        <SelectValue />
      </SelectTrigger>
      <SelectContent>
        <SelectItem value={NONE_VALUE}>Не выбран</SelectItem>
        {courses.map((course) => (
          <SelectItem key={course.id} value={course.id}>
            {course.title}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  );
}
