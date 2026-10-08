"use client";

import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { courseCurriculumQueryOptions } from "@/entities/course";
import type { CurriculumItemDto, CurriculumSectionDto } from "@/entities/course";
import {
  courseLearningStateQueryOptions,
  myQuizAttemptsSummaryQueryOptions,
  type MyQuizAttemptsSummaryItem,
} from "@/entities/course-progress";
import {
  CourseAccessNotice,
  CurriculumSectionCard,
  useResolvedCourseAccess,
} from "@/features/course-learning";
import { routes } from "@/shared/config/routes";
import { useCourseId, useCourseSlug } from "@/shared/providers/course-id-provider";
import { cn } from "@/shared/lib/css";
import { pluralize } from "@/shared/lib/pluralize";
import { Icons } from "@/shared/ui/icons";
import { Button } from "@/shared/ui/kit/button";
import { ProgressDial } from "@/shared/ui/components/progress-dial";
import { EmptyState } from "@/shared/ui/kit/empty-state";
import { Skeleton } from "@/shared/ui/kit/skeleton";
import { usePathname } from "next/navigation";

/**
 * Вкладка «Тесты» (#551, результаты #578) — все quiz-элементы программы курса
 * одним списком. Зеркало вкладки «Задания»: те же curriculum-секции,
 * отфильтрованные до quiz-item'ов; прогресс «пройден» = passed-попытка из
 * learning-state (как на «Программе»), замки для незаписанных рисует
 * CurriculumSectionCard через canAccessItem. Секции свёрнуты по умолчанию (#551).
 *
 * Единственное место с тестами на платформе после #578 (глобальная «Мои тесты»
 * убрана): каждый пройденный тест дополнен результатом текущего пользователя
 * (бейдж «Сдан/Не сдан», лучший балл, попытки, «Пройти ещё раз») — join сводки
 * `my-summary` по `quizId === curriculum item.id`.
 */
export default function CourseTestsPage() {
  const courseId = useCourseId();
  const courseSlug = useCourseSlug();
  const pathname = usePathname();

  const { data: curriculum, isLoading } = useQuery(courseCurriculumQueryOptions(courseId));
  const access = useResolvedCourseAccess(courseId, curriculum?.authorId);
  const { data: learningState, isLoading: learningStateLoading } = useQuery({
    ...courseLearningStateQueryOptions(courseId),
    enabled: access.isAuthenticated && !!courseId,
  });
  // Результаты тестов пользователя (по всем курсам) — фильтруем до тестов этого
  // курса по quizId при рендере строки. Не блокирует загрузку списка.
  const { data: myTests } = useQuery({
    ...myQuizAttemptsSummaryQueryOptions(),
    enabled: access.isAuthenticated,
  });

  const waitingForProgress = access.isAuthenticated && learningStateLoading;
  if (isLoading || waitingForProgress) {
    return (
      <div className="max-w-6xl mx-auto px-4 py-8 md:px-8 space-y-6">
        <div className="space-y-2">
          <Skeleton className="h-10 w-56" />
          <Skeleton className="h-4 w-72" />
        </div>
        <div className="space-y-3 pt-4">
          <Skeleton className="h-16 rounded-2xl" />
          <Skeleton className="h-16 rounded-2xl" />
        </div>
      </div>
    );
  }

  if (!curriculum) return null;

  // Quiz-итемы живут в Module-секциях программы (module_items.item_type='Quiz').
  const quizSections: CurriculumSectionDto[] = curriculum.sections
    .filter((s) => s.itemType === "Module")
    .map((section) => ({
      ...section,
      items: section.items.filter((i) => i.itemType === "Quiz"),
    }))
    .filter((s) => s.items.length > 0);

  // Тест «пройден» = passed-попытка (ST-16 #495); id квиза = id curriculum-итема.
  const quizIds = new Set(quizSections.flatMap((s) => s.items.map((i) => i.id)));
  const totalQuizzes = quizIds.size;
  const passedQuizzes =
    learningState?.passedQuizIds?.filter((quizId) => quizIds.has(quizId)).length ?? 0;
  const overallPercent = totalQuizzes > 0 ? Math.round((passedQuizzes / totalQuizzes) * 100) : 0;

  const showProgress = access.isAuthenticated && learningState !== undefined && totalQuizzes > 0;

  const stats = [
    { value: totalQuizzes, label: "тестов" },
    ...(showProgress ? [{ value: passedQuizzes, label: "пройдено" }] : []),
  ];

  // quizId сводки почти всегда === id curriculum-итема квиза (reference_id
  // module_item'а, #495); lookup ниже с фолбэком на item.id для robustness.
  const resultByQuizId = new Map((myTests?.items ?? []).map((it) => [it.quizId, it]));
  const renderTrailing = access.isAuthenticated
    ? (item: CurriculumItemDto) => {
        const result = resultByQuizId.get(item.quizId ?? item.id);
        if (!result) return null;
        return (
          <QuizResultTrailing
            result={result}
            href={routes.courseQuiz(courseSlug, item.quizId ?? item.id)}
          />
        );
      }
    : undefined;

  return (
    <div className="max-w-6xl mx-auto px-4 py-8 md:px-8 pb-16">
      <header className="mb-5 md:mb-6">
        <div className="flex flex-col gap-4 md:flex-row md:items-start md:justify-between">
          <div className="min-w-0">
            <h1 className="text-2xl md:text-3xl font-bold tracking-tight text-foreground">
              Тесты
            </h1>
            <p className="mt-1.5 text-sm text-muted-foreground tabular-nums">
              {totalQuizzes} {pluralize(totalQuizzes, "тест", "теста", "тестов")}
              {showProgress && (
                <>
                  <span className="mx-2 text-muted-foreground/40">·</span>
                  {passedQuizzes} пройдено
                </>
              )}
            </p>
          </div>

          {totalQuizzes > 0 && (
            <dl className="flex items-stretch divide-x divide-border/50 rounded-xl border border-border/50 bg-card/30 shrink-0">
              {stats.map((s) => (
                <div
                  key={s.label}
                  className="flex flex-col items-center justify-center px-4 py-2 sm:px-5 sm:py-2.5 min-w-[76px]"
                >
                  <dt className="order-2 mt-0.5 text-[10px] uppercase tracking-[0.14em] text-muted-foreground/70">
                    {s.label}
                  </dt>
                  <dd className="order-1 text-lg sm:text-xl font-bold tabular-nums leading-none text-foreground">
                    {s.value}
                  </dd>
                </div>
              ))}
              {showProgress && (
                <div className="flex items-center justify-center px-3 sm:px-4">
                  <ProgressDial percent={overallPercent} size={56} strokeWidth={5} />
                </div>
              )}
            </dl>
          )}
        </div>
      </header>

      <CourseAccessNotice accessLevel={access.accessLevel} className="mb-4" />

      {quizSections.length === 0 ? (
        <EmptyState
          icon={Icons.quiz}
          title="Тестов в курсе пока нет"
          variant="dashed"
        />
      ) : (
        <div className="space-y-2">
          {quizSections.map((section, index) => (
            <CurriculumSectionCard
              key={section.id}
              section={section}
              sectionNumber={index + 1}
              sectionKind="module"
              itemFilter="quiz"
              learningState={learningState}
              accessLevel={access.accessLevel}
              courseSlug={courseSlug}
              defaultOpen={false}
              currentPath={pathname}
              renderItemTrailing={renderTrailing}
            />
          ))}
        </div>
      )}
    </div>
  );
}

/**
 * Результат прохождения теста в строке курсовой вкладки «Тесты» (#578).
 * Рендерится в trailing-слот `CurriculumSectionCard` (вне основной ссылки строки)
 * только для тестов, у которых есть попытки.
 */
function QuizResultTrailing({
  result,
  href,
}: {
  result: MyQuizAttemptsSummaryItem;
  href: string;
}) {
  return (
    <div className="flex items-center gap-2 sm:gap-2.5">
      <PassPill passed={result.passed} />
      <span className="hidden md:inline text-[11px] tabular-nums text-muted-foreground/70">
        лучш. {result.bestScorePercent}%
        <span className="mx-1 text-muted-foreground/30">·</span>
        {result.attemptsCount} {pluralize(result.attemptsCount, "попытка", "попытки", "попыток")}
      </span>
      <Button asChild size="sm" variant="outline" className="h-7 shrink-0 px-2 text-xs">
        <Link href={href}>
          <span className="hidden sm:inline">Пройти ещё раз</span>
          <span className="sm:hidden">Ещё раз</span>
        </Link>
      </Button>
    </div>
  );
}

function PassPill({ passed }: { passed: boolean }) {
  return (
    <span
      className={cn(
        "inline-flex shrink-0 items-center gap-1 rounded-full border px-1.5 py-0.5 text-[10px] font-semibold uppercase tracking-[0.08em]",
        passed
          ? "border-emerald-500/40 bg-emerald-500/10 text-emerald-600 dark:text-emerald-400"
          : "border-amber-500/40 bg-amber-500/10 text-amber-600 dark:text-amber-400",
      )}
    >
      {passed ? <Icons.completed className="size-3" /> : <Icons.clock className="size-3" />}
      {passed ? "Сдан" : "Не сдан"}
    </span>
  );
}
