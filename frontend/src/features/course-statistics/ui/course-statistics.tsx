"use client";

import {
  courseProgressStatsOptions,
  courseQuizStatsDetailOptions,
  courseQuizStatsOverviewOptions,
  type CourseQuizStatsRow,
} from "../api";
import type { CourseBuilderDto } from "@/entities/course";
import { routes } from "@/shared/config/routes";
import { cn } from "@/shared/lib/css";
import { KpiCard } from "@/shared/ui/components";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/ui/kit/card";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@/shared/ui/kit/dialog";
import { EmptyState } from "@/shared/ui/kit/empty-state";
import { Icons } from "@/shared/ui/icons";
import { Skeleton } from "@/shared/ui/kit/skeleton";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/shared/ui/kit/table";
import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { useState } from "react";

const numberFormatter = new Intl.NumberFormat("ru");

function passRateClass(percent: number): string {
  if (percent < 40) return "text-rose-500";
  if (percent < 70) return "text-amber-500";
  return "text-emerald-500";
}

interface CourseStatisticsProps {
  courseId: string;
  course: CourseBuilderDto;
}

/**
 * Авторская статистика по одному курсу (#634): KPI-полоса прохождения курса
 * студентами (вовлечённость / завершения / средний прогресс / отметки «изучено»
 * / сдачи заданий / прошли тест) + таблица тестов курса (доля зачётов, средний
 * балл, уникальные юзеры) с drill-in по клику — распределение баллов и самые
 * сложные вопросы. Зеркалит глобальную админ-страницу статистики тестов, но в
 * рамках одного курса. Author-scoped.
 */
export function CourseStatistics({ courseId, course }: CourseStatisticsProps) {
  const progressQuery = useQuery(courseProgressStatsOptions(courseId));
  const overviewQuery = useQuery(courseQuizStatsOverviewOptions(courseId));
  const [activeQuiz, setActiveQuiz] = useState<CourseQuizStatsRow | null>(null);

  const progress = progressQuery.data;
  const overview = overviewQuery.data;
  const quizzes = overview?.quizzes ?? [];

  return (
    <div className="space-y-6">
      <div>
        <h2 className="text-xl font-bold tracking-tight">Статистика курса</h2>
        <p className="text-sm text-muted-foreground">
          Как студенты проходят «{course.title}» — прогресс и тесты
        </p>
      </div>

      {/* KPI-полоса прохождения курса */}
      <div className="grid grid-cols-2 gap-4 lg:grid-cols-3">
        <KpiCard
          label="Вовлечённых студентов"
          value={progress ? numberFormatter.format(progress.engagedStudents) : "—"}
          isLoading={progressQuery.isLoading}
        />
        <KpiCard
          label="Завершивших"
          value={progress ? numberFormatter.format(progress.completedStudents) : "—"}
          isLoading={progressQuery.isLoading}
        />
        <KpiCard
          label="Средний прогресс"
          value={progress ? `${progress.averageProgressPercent}%` : "—"}
          isLoading={progressQuery.isLoading}
        />
        <KpiCard
          label="Отметок «изучено»"
          value={progress ? numberFormatter.format(progress.materialViewsCompleted) : "—"}
          isLoading={progressQuery.isLoading}
        />
        <KpiCard
          label="Сдач заданий"
          value={progress ? numberFormatter.format(progress.issueSubmissionsCount) : "—"}
          isLoading={progressQuery.isLoading}
        />
        <KpiCard
          label="Прошли тест"
          value={progress ? numberFormatter.format(progress.quizPassersCount) : "—"}
          isLoading={progressQuery.isLoading}
        />
      </div>

      {/* Тесты курса */}
      <Card>
        <CardHeader>
          <CardTitle className="text-base">
            Тесты курса
            {overview && (
              <span className="ml-2 font-normal text-muted-foreground">
                {numberFormatter.format(overview.totalQuizzesWithAttempts)}{" "}
                {overview.totalQuizzesWithAttempts === 1 ? "тест" : "тестов"} с попытками ·{" "}
                {numberFormatter.format(overview.totalAttempts)} попыток · зачётов{" "}
                {overview.overallPassRatePercent}% · ср. балл {overview.overallAvgScorePercent}%
              </span>
            )}
          </CardTitle>
        </CardHeader>
        <CardContent>
          {overviewQuery.isLoading && <Skeleton className="h-64 w-full" />}

          {overview && quizzes.length === 0 && (
            <EmptyState
              variant="plain"
              icon={Icons.searchEmpty}
              title="Тесты ещё никто не проходил"
              description="Как только студенты начнут проходить тесты этого курса, статистика появится здесь."
            />
          )}

          {quizzes.length > 0 && (
            <>
              {/* Desktop-таблица */}
              <div className="hidden md:block">
                <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead>Тест</TableHead>
                      <TableHead className="text-right">Попыток</TableHead>
                      <TableHead className="text-right">Юзеров</TableHead>
                      <TableHead className="text-right">Зачётов</TableHead>
                      <TableHead className="text-right">Ср. балл</TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {quizzes.map((quiz) => (
                      <TableRow
                        key={quiz.quizId}
                        className="cursor-pointer"
                        onClick={() => setActiveQuiz(quiz)}
                      >
                        <TableCell className="font-medium">{quiz.title}</TableCell>
                        <TableCell className="text-right font-mono tabular-nums">
                          {numberFormatter.format(quiz.attemptsCount)}
                        </TableCell>
                        <TableCell className="text-right font-mono tabular-nums text-muted-foreground">
                          {numberFormatter.format(quiz.uniqueUsers)}
                        </TableCell>
                        <TableCell
                          className={cn(
                            "text-right font-mono tabular-nums",
                            passRateClass(quiz.passRatePercent),
                          )}
                        >
                          {quiz.passRatePercent}%
                        </TableCell>
                        <TableCell className="text-right font-mono tabular-nums">
                          {quiz.avgScorePercent}%
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </div>

              {/* Mobile-карточки */}
              <div className="space-y-2 md:hidden">
                {quizzes.map((quiz) => (
                  <button
                    key={quiz.quizId}
                    type="button"
                    onClick={() => setActiveQuiz(quiz)}
                    className="min-h-[44px] w-full rounded-lg border border-border/60 bg-card/50 p-3 text-left text-sm"
                  >
                    <div className="flex items-center justify-between gap-2">
                      <span className="font-medium">{quiz.title}</span>
                      <span
                        className={cn(
                          "font-mono tabular-nums",
                          passRateClass(quiz.passRatePercent),
                        )}
                      >
                        {quiz.passRatePercent}%
                      </span>
                    </div>
                    <div className="mt-1 flex items-center justify-between gap-2 text-xs text-muted-foreground">
                      <span>
                        {numberFormatter.format(quiz.attemptsCount)} попыток ·{" "}
                        {numberFormatter.format(quiz.uniqueUsers)} юзеров
                      </span>
                      <span>ср. {quiz.avgScorePercent}%</span>
                    </div>
                  </button>
                ))}
              </div>
            </>
          )}
        </CardContent>
      </Card>

      <QuizDrillInDialog
        courseId={courseId}
        quiz={activeQuiz}
        onClose={() => setActiveQuiz(null)}
      />
    </div>
  );
}

function QuizDrillInDialog({
  courseId,
  quiz,
  onClose,
}: {
  courseId: string;
  quiz: CourseQuizStatsRow | null;
  onClose: () => void;
}) {
  const statsQuery = useQuery(courseQuizStatsDetailOptions(courseId, quiz?.quizId ?? null));
  const stats = statsQuery.data;

  const maxBucketCount = Math.max(1, ...(stats?.scoreDistribution.map((b) => b.count) ?? [0]));
  // Самые сложные вопросы — сверху (наименьшая доля верных среди ответивших).
  const hardestQuestions = stats
    ? [...stats.questions].sort((a, b) => a.correctRatePercent - b.correctRatePercent)
    : [];

  return (
    <Dialog open={!!quiz} onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="max-h-[85vh] overflow-y-auto sm:max-w-2xl">
        <DialogHeader>
          <DialogTitle>{quiz?.title ?? "Тест"}</DialogTitle>
          <DialogDescription>Разбор по баллам и по сложности вопросов</DialogDescription>
          {quiz && (
            <Link
              href={routes.authorQuizEdit(quiz.quizId)}
              target="_blank"
              rel="noopener"
              className="mt-1 inline-flex w-fit items-center gap-1.5 text-sm font-medium text-primary hover:underline"
            >
              <Icons.edit className="size-4" />
              Редактировать тест
            </Link>
          )}
        </DialogHeader>

        {statsQuery.isLoading && <Skeleton className="h-64 w-full" />}

        {stats && (
          <div className="space-y-6">
            <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
              <Metric label="Попыток" value={numberFormatter.format(stats.attemptsCount)} />
              <Metric label="Юзеров" value={numberFormatter.format(stats.uniqueUsers)} />
              <Metric label="Зачётов" value={`${stats.passRatePercent}%`} />
              <Metric label="Ср. балл" value={`${stats.avgScorePercent}%`} />
            </div>

            <div>
              <h3 className="mb-3 text-sm font-medium">Распределение баллов</h3>
              <div className="space-y-2.5">
                {stats.scoreDistribution.map((bucket) => (
                  <div key={bucket.bucket} className="flex items-center gap-3 text-sm">
                    <span className="w-16 shrink-0 text-muted-foreground">{bucket.bucket}%</span>
                    <div className="h-2.5 flex-1 overflow-hidden rounded-full bg-border/50">
                      <div
                        className="h-full rounded-full bg-primary/70"
                        style={{ width: `${(bucket.count / maxBucketCount) * 100}%` }}
                      />
                    </div>
                    <span className="w-10 shrink-0 text-right font-mono tabular-nums">
                      {numberFormatter.format(bucket.count)}
                    </span>
                  </div>
                ))}
              </div>
            </div>

            <div>
              <h3 className="mb-3 text-sm font-medium">Самые сложные вопросы</h3>
              {hardestQuestions.length === 0 && (
                <p className="text-sm text-muted-foreground">У теста нет вопросов</p>
              )}
              <div className="space-y-2.5">
                {hardestQuestions.map((question) => (
                  <div key={question.questionId} className="flex items-center gap-3 text-sm">
                    <span
                      className="min-w-0 flex-1 truncate text-muted-foreground"
                      title={question.text}
                    >
                      {question.text}
                    </span>
                    <div className="h-2.5 w-24 shrink-0 overflow-hidden rounded-full bg-border/50">
                      <div
                        className={cn(
                          "h-full rounded-full",
                          question.answeredCount === 0
                            ? "bg-border"
                            : question.correctRatePercent < 40
                              ? "bg-rose-500/70"
                              : question.correctRatePercent < 70
                                ? "bg-amber-500/70"
                                : "bg-emerald-500/70",
                        )}
                        style={{ width: `${Math.min(question.correctRatePercent, 100)}%` }}
                      />
                    </div>
                    <span className="w-12 shrink-0 text-right font-mono tabular-nums">
                      {question.answeredCount === 0 ? "—" : `${question.correctRatePercent}%`}
                    </span>
                  </div>
                ))}
              </div>
            </div>
          </div>
        )}
      </DialogContent>
    </Dialog>
  );
}

function Metric({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-lg border border-border/60 bg-card/50 p-3">
      <div className="text-xs text-muted-foreground">{label}</div>
      <div className="mt-1 text-lg font-semibold tabular-nums">{value}</div>
    </div>
  );
}
