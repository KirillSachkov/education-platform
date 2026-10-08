"use client";

import { useQuery } from "@tanstack/react-query";

import {
  type AdminFeedbackRatingItem,
  adminFeedbackRatingStatsQueryOptions,
  type TrainerAdminStatsRange,
} from "@/entities/trainer-admin-stats";
import { cn } from "@/shared/lib/css";
import { Icons } from "@/shared/ui/icons";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/shared/ui/kit/table";

import { sortByDownRate } from "../lib/ai-feedback";
import {
  AdminStatCard,
  AdminStatSection,
  DIFFICULTY_LABELS,
  EmptyRow,
  int,
  pct,
  StatsErrorState,
  TabSkeleton,
} from "./primitives";

/** down-rate >= этого порога красит долю 👎 в destructive (сигнал «разбор не нравится»). */
const HIGH_DOWN_RATE = 0.5;

/**
 * Разбор ИИ (#691 t7): по-вопросная 👍/👎 оценка AI-разбора, «худшие сверху» (наибольший
 * down-rate) — владелец видит вопросы-кандидаты на правку промпта/эталона. Лениво грузит
 * запрос только когда вкладка активна (Radix размонтирует неактивный контент).
 */
export function AiFeedbackTab({ days }: { days: TrainerAdminStatsRange }) {
  const { data, isLoading, error } = useQuery(adminFeedbackRatingStatsQueryOptions(days));

  if (error) return <StatsErrorState error={error} />;
  if (isLoading || !data) return <TabSkeleton />;

  const sorted = sortByDownRate(data.questions);
  const totalUp = data.questions.reduce((s, q) => s + q.up, 0);
  const totalDown = data.questions.reduce((s, q) => s + q.down, 0);
  const totalRatings = totalUp + totalDown;
  const overallDownRate = totalRatings > 0 ? totalDown / totalRatings : null;

  return (
    <div className="space-y-4">
      <section className="grid grid-cols-2 gap-3 md:grid-cols-4">
        <AdminStatCard
          label="Вопросов с оценками"
          value={data.questions.length}
          icon={<Icons.ai className="size-4" />}
          index={0}
        />
        <AdminStatCard
          label="Лайков 👍"
          value={totalUp}
          icon={<Icons.thumbsUp className="size-4" />}
          index={1}
        />
        <AdminStatCard
          label="Дизлайков 👎"
          value={totalDown}
          icon={<Icons.thumbsDown className="size-4" />}
          accentClass={totalDown > 0 ? "text-destructive" : undefined}
          index={2}
        />
        <AdminStatCard
          label="Доля 👎"
          value={overallDownRate == null ? 0 : Math.round(overallDownRate * 100)}
          suffix="%"
          icon={<Icons.warning className="size-4" />}
          accentClass={
            overallDownRate != null && overallDownRate >= HIGH_DOWN_RATE
              ? "text-destructive"
              : undefined
          }
          index={3}
        />
      </section>

      <AdminStatSection
        title="Разбор ИИ"
        icon={<Icons.ai className="size-4" />}
        hint="Оценки 👍/👎 студентов на AI-разбор открытых ответов. Сверху — вопросы с наибольшей долей 👎: кандидаты на правку промпта или эталонного ответа."
      >
        {data.questions.length === 0 ? (
          <EmptyRow text="Пока нет оценок ИИ-разбора" />
        ) : (
          <>
            <div className="hidden md:block">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Вопрос</TableHead>
                    <TableHead className="text-right">👍</TableHead>
                    <TableHead className="text-right">👎</TableHead>
                    <TableHead className="text-right">Доля 👎</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {sorted.map((q) => (
                    <TableRow key={q.questionId}>
                      <TableCell className="max-w-[420px]">
                        <QuestionStem q={q} />
                      </TableCell>
                      <TableCell className="text-right tabular-nums">{int.format(q.up)}</TableCell>
                      <TableCell className="text-right tabular-nums">{int.format(q.down)}</TableCell>
                      <TableCell
                        className={cn(
                          "text-right tabular-nums",
                          q.downRate >= HIGH_DOWN_RATE ? "font-medium text-destructive" : undefined,
                        )}
                      >
                        {pct(q.downRate)}
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </div>

            <ul className="space-y-2 md:hidden">
              {sorted.map((q) => (
                <li key={q.questionId} className="rounded-lg border bg-card p-3">
                  <QuestionStem q={q} />
                  <dl className="mt-2 grid grid-cols-3 gap-x-3 gap-y-1 text-xs">
                    <Metric label="👍" value={int.format(q.up)} />
                    <Metric label="👎" value={int.format(q.down)} />
                    <Metric
                      label="Доля 👎"
                      value={pct(q.downRate)}
                      accent={q.downRate >= HIGH_DOWN_RATE}
                    />
                  </dl>
                </li>
              ))}
            </ul>
          </>
        )}
      </AdminStatSection>
    </div>
  );
}

function QuestionStem({ q }: { q: AdminFeedbackRatingItem }) {
  return (
    <div className="min-w-0">
      <span className="line-clamp-2 text-sm">{q.stem}</span>
      <div className="mt-0.5 flex flex-wrap items-center gap-x-2 gap-y-0.5 text-2xs text-muted-foreground">
        <span className="truncate">{q.topicTitle}</span>
        <span aria-hidden>·</span>
        <span className="font-mono">{q.questionType}</span>
        {q.difficulty ? (
          <>
            <span aria-hidden>·</span>
            <span>{DIFFICULTY_LABELS[q.difficulty] ?? q.difficulty}</span>
          </>
        ) : null}
      </div>
    </div>
  );
}

function Metric({ label, value, accent }: { label: string; value: string; accent?: boolean }) {
  return (
    <div className="flex items-center justify-between gap-2">
      <dt className="text-muted-foreground">{label}</dt>
      <dd className={cn("font-medium tabular-nums", accent ? "text-destructive" : undefined)}>
        {value}
      </dd>
    </div>
  );
}
