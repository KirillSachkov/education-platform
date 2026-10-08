"use client";

import { adminQuizStatsQueryOptions, type QuizAdminOverviewRow } from "../api";
import { groupQuizzesByCourse } from "../model/group-by-course";
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
import { useState } from "react";

const numberFormatter = new Intl.NumberFormat("ru");

function passRateClass(percent: number): string {
  if (percent < 40) return "text-rose-500";
  if (percent < 70) return "text-amber-500";
  return "text-emerald-500";
}

/**
 * Админ-аналитика по всем тестам платформы (#556, AC5): KPI (число тестов, попыток,
 * доля зачётов, средний балл), тесты сгруппированы по курсам (доля зачётов / средний
 * балл / уникальные юзеры на тест) и drill-in по клику на тест — распределение баллов
 * по бакетам + самые сложные вопросы (доля верных ответов). LEVEL_TEST исключён бэком
 * (у воронки своя страница, #537).
 */
export function AdminQuizStatsPage() {
  const overviewQuery = useQuery(adminQuizStatsQueryOptions.getOverviewOptions());
  const [activeQuiz, setActiveQuiz] = useState<QuizAdminOverviewRow | null>(null);

  const overview = overviewQuery.data;
  const isLoading = overviewQuery.isLoading;
  const groups = overview ? groupQuizzesByCourse(overview.quizzes) : [];

  return (
    <div className="mx-auto max-w-6xl space-y-6 p-4 md:p-6">
      <div>
        <h1 className="text-2xl font-bold tracking-tight">Статистика тестов</h1>
        <p className="text-sm text-muted-foreground">
          Как студенты проходят тесты в курсах — по каждому тесту и каждому вопросу
        </p>
      </div>

      <div className="grid grid-cols-2 gap-4 lg:grid-cols-4">
        <KpiCard
          label="Тестов с попытками"
          value={overview ? numberFormatter.format(overview.totalQuizzes) : "—"}
          isLoading={isLoading}
        />
        <KpiCard
          label="Всего попыток"
          value={overview ? numberFormatter.format(overview.totalAttempts) : "—"}
          isLoading={isLoading}
        />
        <KpiCard
          label="Доля зачётов"
          value={overview ? `${overview.overallPassRatePercent}%` : "—"}
          isLoading={isLoading}
        />
        <KpiCard
          label="Средний балл"
          value={overview ? `${overview.overallAvgScorePercent}%` : "—"}
          isLoading={isLoading}
        />
      </div>

      {isLoading && <Skeleton className="h-64 w-full" />}

      {overview && groups.length === 0 && (
        <Card>
          <CardContent className="p-6">
            <EmptyState
              variant="plain"
              icon={Icons.searchEmpty}
              title="Тесты ещё никто не проходил"
              description="Как только студенты начнут проходить тесты, статистика появится здесь."
            />
          </CardContent>
        </Card>
      )}

      {groups.map((group) => (
        <Card key={group.courseId ?? "__none__"}>
          <CardHeader>
            <CardTitle className="text-base">
              {group.courseTitle}
              <span className="ml-2 font-normal text-muted-foreground">
                {numberFormatter.format(group.quizzes.length)}{" "}
                {group.quizzes.length === 1 ? "тест" : "тестов"}
              </span>
            </CardTitle>
          </CardHeader>
          <CardContent>
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
                  {group.quizzes.map((quiz) => (
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
              {group.quizzes.map((quiz) => (
                <button
                  key={quiz.quizId}
                  type="button"
                  onClick={() => setActiveQuiz(quiz)}
                  className="min-h-[44px] w-full rounded-lg border border-border/60 bg-card/50 p-3 text-left text-sm"
                >
                  <div className="flex items-center justify-between gap-2">
                    <span className="font-medium">{quiz.title}</span>
                    <span className={cn("font-mono tabular-nums", passRateClass(quiz.passRatePercent))}>
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
          </CardContent>
        </Card>
      ))}

      <QuizDrillInDialog quiz={activeQuiz} onClose={() => setActiveQuiz(null)} />
    </div>
  );
}

function QuizDrillInDialog({
  quiz,
  onClose,
}: {
  quiz: QuizAdminOverviewRow | null;
  onClose: () => void;
}) {
  const statsQuery = useQuery(adminQuizStatsQueryOptions.getStatsOptions(quiz?.quizId ?? null));
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
