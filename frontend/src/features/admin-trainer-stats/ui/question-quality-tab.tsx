"use client";

import { useQuery } from "@tanstack/react-query";
import { useState } from "react";

import {
  type AdminOpenTextQuality,
  type AdminQuestionQualityItem,
  adminQuestionQualityQueryOptions,
  type TrainerAdminStatsRange,
} from "@/entities/trainer-admin-stats";
import { cn } from "@/shared/lib/css";
import { SegmentedControl } from "@/shared/ui/components";
import { Icons } from "@/shared/ui/icons";
import { Button } from "@/shared/ui/kit/button";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/shared/ui/kit/table";

import {
  defaultDirFor,
  type QualitySortKey,
  shouldFlagRewrite,
  type SortDir,
  sortQuestions,
} from "../lib/question-quality";
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

const SORT_LABELS: Record<QualitySortKey, string> = {
  correctRate: "%-верных",
  discrimination: "Дискрим.",
  skipRate: "Skip",
  attempts: "Попыток",
};

/** Качество вопросов (#681 T4): сортируемая таблица + флаг «переписать» + OPEN_TEXT-разбивка. */
export function QuestionQualityTab({ days }: { days: TrainerAdminStatsRange }) {
  const { data, isLoading, error } = useQuery(adminQuestionQualityQueryOptions(days));
  const [sortKey, setSortKey] = useState<QualitySortKey>("correctRate");
  const [sortDir, setSortDir] = useState<SortDir>("asc");

  if (error) return <StatsErrorState error={error} />;
  if (isLoading || !data) return <TabSkeleton />;

  const sorted = sortQuestions(data.questions, sortKey, sortDir);
  const flaggedCount = data.questions.filter((q) => shouldFlagRewrite(q)).length;

  function applySort(key: QualitySortKey) {
    if (key === sortKey) {
      setSortDir(sortDir === "asc" ? "desc" : "asc");
    } else {
      setSortKey(key);
      setSortDir(defaultDirFor(key));
    }
  }

  return (
    <div className="space-y-4">
      <section className="grid grid-cols-2 gap-3 md:grid-cols-3">
        <AdminStatCard
          label="Вопросов с ответами"
          value={data.questions.length}
          icon={<Icons.listChecks className="size-4" />}
          index={0}
        />
        <AdminStatCard
          label="Флаг «переписать»"
          value={flaggedCount}
          icon={<Icons.warning className="size-4" />}
          accentClass={flaggedCount > 0 ? "text-destructive" : undefined}
          index={1}
        />
        <AdminStatCard
          label="Окно, дней"
          value={data.days}
          icon={<Icons.calendar className="size-4" />}
          index={2}
        />
      </section>

      <AdminStatSection
        title="Качество вопросов"
        icon={<Icons.quiz className="size-4" />}
        hint={`Флажим «переписать» при низком %-верных и достаточной выборке. Время на вопрос — приближение (отсечка ${data.outlierCapSeconds} с).`}
        action={
          <div className="hidden items-center gap-2 md:flex">
            <SortControl sortKey={sortKey} onSort={applySort} dir={sortDir} />
          </div>
        }
      >
        {/* Mobile sort control */}
        <div className="mb-3 md:hidden">
          <SortControl sortKey={sortKey} onSort={applySort} dir={sortDir} />
        </div>

        {data.questions.length === 0 ? (
          <EmptyRow text="Нет ответов на вопросы за период." />
        ) : (
          <>
            <div className="hidden md:block">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Вопрос</TableHead>
                    <SortableHead label={SORT_LABELS.attempts} k="attempts" sortKey={sortKey} dir={sortDir} onSort={applySort} />
                    <SortableHead label={SORT_LABELS.correctRate} k="correctRate" sortKey={sortKey} dir={sortDir} onSort={applySort} />
                    <SortableHead label={SORT_LABELS.discrimination} k="discrimination" sortKey={sortKey} dir={sortDir} onSort={applySort} />
                    <SortableHead label={SORT_LABELS.skipRate} k="skipRate" sortKey={sortKey} dir={sortDir} onSort={applySort} />
                    <TableHead className="text-right">Время</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {sorted.map((q) => (
                    <TableRow key={q.questionId}>
                      <TableCell className="max-w-[360px]">
                        <QuestionStem q={q} />
                      </TableCell>
                      <TableCell className="text-right tabular-nums">{int.format(q.attempts)}</TableCell>
                      <TableCell className="text-right tabular-nums">{pct(q.correctRate)}</TableCell>
                      <TableCell className="text-right tabular-nums">
                        {formatDiscrimination(q.discrimination)}
                      </TableCell>
                      <TableCell className="text-right tabular-nums">{pct(q.skipRate)}</TableCell>
                      <TableCell className="text-right tabular-nums">
                        {formatTime(q.avgSecondsPerQuestion)}
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
                  <dl className="mt-2 grid grid-cols-2 gap-x-3 gap-y-1 text-xs">
                    <Metric label="Попыток" value={int.format(q.attempts)} />
                    <Metric label="%-верных" value={pct(q.correctRate)} />
                    <Metric label="Дискрим." value={formatDiscrimination(q.discrimination)} />
                    <Metric label="Skip" value={pct(q.skipRate)} />
                    <Metric label="Время" value={formatTime(q.avgSecondsPerQuestion)} />
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

function SortControl({
  sortKey,
  dir,
  onSort,
}: {
  sortKey: QualitySortKey;
  dir: SortDir;
  onSort: (k: QualitySortKey) => void;
}) {
  return (
    <div className="flex items-center gap-2">
      <SegmentedControl
        ariaLabel="Сортировка вопросов"
        variant="subtle"
        value={sortKey}
        onChange={onSort}
        options={(Object.keys(SORT_LABELS) as QualitySortKey[]).map((k) => ({
          value: k,
          label: SORT_LABELS[k],
        }))}
      />
      <Button
        type="button"
        variant="outline"
        size="icon"
        className="size-9 shrink-0"
        aria-label={dir === "asc" ? "По возрастанию" : "По убыванию"}
        onClick={() => onSort(sortKey)}
      >
        {dir === "asc" ? (
          <Icons.chevronUp className="size-4" />
        ) : (
          <Icons.chevronDown className="size-4" />
        )}
      </Button>
    </div>
  );
}

function SortableHead({
  label,
  k,
  sortKey,
  dir,
  onSort,
}: {
  label: string;
  k: QualitySortKey;
  sortKey: QualitySortKey;
  dir: SortDir;
  onSort: (k: QualitySortKey) => void;
}) {
  const active = k === sortKey;
  return (
    <TableHead className="text-right">
      <button
        type="button"
        onClick={() => onSort(k)}
        className={cn(
          "ml-auto inline-flex items-center gap-1 hover:text-foreground",
          active ? "text-foreground" : "text-muted-foreground",
        )}
      >
        {label}
        {active ? (
          dir === "asc" ? (
            <Icons.chevronUp className="size-3" />
          ) : (
            <Icons.chevronDown className="size-3" />
          )
        ) : (
          <Icons.chevronsUpDown className="size-3 opacity-50" />
        )}
      </button>
    </TableHead>
  );
}

function QuestionStem({ q }: { q: AdminQuestionQualityItem }) {
  const flagged = shouldFlagRewrite(q);
  return (
    <div className="min-w-0">
      <div className="flex items-start gap-2">
        <span className="line-clamp-2 text-sm">{q.stem}</span>
        {flagged ? (
          <span className="inline-flex shrink-0 items-center gap-1 rounded-md bg-destructive/10 px-1.5 py-0.5 text-2xs font-medium text-destructive">
            <Icons.warning className="size-3" aria-hidden />
            переписать
          </span>
        ) : null}
      </div>
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
      {q.openText ? <OpenTextSplit ot={q.openText} /> : null}
    </div>
  );
}

/** OPEN_TEXT: разбивка вердиктов + распределение балла по бэндам (#678). */
function OpenTextSplit({ ot }: { ot: AdminOpenTextQuality }) {
  return (
    <div className="mt-1 flex flex-wrap items-center gap-x-2 gap-y-0.5 text-2xs">
      <span className="text-green">✓ {int.format(ot.correct)}</span>
      <span className="text-amber-600 dark:text-amber-400">~ {int.format(ot.partial)}</span>
      <span className="text-destructive">✗ {int.format(ot.incorrect)}</span>
      <span className="text-muted-foreground" title="Балл: 0–39 / 40–79 / 80–100">
        балл {int.format(ot.scoreBucketLow)}/{int.format(ot.scoreBucketMid)}/
        {int.format(ot.scoreBucketHigh)}
      </span>
    </div>
  );
}

function Metric({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex items-center justify-between gap-2">
      <dt className="text-muted-foreground">{label}</dt>
      <dd className="font-medium tabular-nums">{value}</dd>
    </div>
  );
}

/** Дискриминативность: знаковая, 2 знака (или «—» при null). */
function formatDiscrimination(d: number | null): string {
  if (d == null) return "—";
  const sign = d > 0 ? "+" : d < 0 ? "−" : "";
  return `${sign}${Math.abs(d).toFixed(2)}`;
}

/** Среднее время на вопрос (секунды → «N с»; «—» при null). */
function formatTime(s: number | null): string {
  if (s == null) return "—";
  return `${Math.round(s)} с`;
}
