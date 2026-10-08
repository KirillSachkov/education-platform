"use client";

import { useQuery } from "@tanstack/react-query";

import {
  type AdminBankStat,
  type AdminCalibrationLevel,
  type AdminMiscalibratedQuestion,
  type AdminTopicStat,
  adminTopicBankStatsQueryOptions,
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

import {
  AdminStatSection,
  DIFFICULTY_LABELS,
  EmptyRow,
  int,
  StatsErrorState,
  TabSkeleton,
} from "./primitives";

/** 0..100 число → «NN%». */
const pctNum = (n: number) => `${Math.round(n)}%`;

const PURPOSE_LABELS: Record<string, string> = { STUDY: "Изучение", MOCK: "Мок" };

/** Темы и банки (#681 T5): per-topic mastery/% + per-bank coverage + калибровка declared vs actual. */
export function TopicsTab({ days }: { days: TrainerAdminStatsRange }) {
  const { data, isLoading, error } = useQuery(adminTopicBankStatsQueryOptions(days));

  if (error) return <StatsErrorState error={error} />;
  if (isLoading || !data) return <TabSkeleton />;

  return (
    <div className="space-y-4">
      <AdminStatSection
        title="Темы"
        icon={<Icons.layers className="size-4" />}
        hint="Mastery — текущий снимок; %-верных и объём — за окно. Сложные темы (низкий %-верных) — сверху."
      >
        <TopicsBlock topics={data.topics} />
      </AdminStatSection>

      <AdminStatSection
        title="Банки вопросов"
        icon={<Icons.library className="size-4" />}
        hint="Покрытие = доля вопросов банка, по которым реально отвечали за окно. Низкое — банк простаивает."
      >
        <BanksBlock banks={data.banks} />
      </AdminStatSection>

      <AdminStatSection
        title="Калибровка сложности"
        icon={<Icons.target className="size-4" />}
        hint="Заявленный уровень vs фактический %-верных. JUNIOR с низким % — на деле трудный; SENIOR с высоким — лёгкий."
      >
        {data.calibration.levels.length === 0 ? (
          <EmptyRow text="Нет оценённых ответов за период." />
        ) : (
          <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
            {data.calibration.levels.map((lvl) => (
              <CalibrationLevelCard key={lvl.difficulty} level={lvl} />
            ))}
          </div>
        )}
        <MiscalibratedList rows={data.calibration.miscalibrated} />
      </AdminStatSection>
    </div>
  );
}

function TopicsBlock({ topics }: { topics: AdminTopicStat[] }) {
  if (topics.length === 0) return <EmptyRow text="Нет тем с активностью за период." />;
  return (
    <>
      <div className="hidden md:block">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Тема</TableHead>
              <TableHead className="text-right">Mastery</TableHead>
              <TableHead className="text-right">%-верных</TableHead>
              <TableHead className="text-right">Ответов</TableHead>
              <TableHead className="text-right">Сессий</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {topics.map((t) => (
              <TableRow key={t.topicId}>
                <TableCell className="max-w-[280px]">
                  <span className="line-clamp-1">{t.topicTitle}</span>
                </TableCell>
                <TableCell className="text-right">
                  <PercentCell value={t.avgMasteryPercent} />
                </TableCell>
                <TableCell className="text-right">
                  <PercentCell value={t.avgCorrectPercent} />
                </TableCell>
                <TableCell className="text-right tabular-nums">{int.format(t.answersCount)}</TableCell>
                <TableCell className="text-right tabular-nums">{int.format(t.sessionsCount)}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </div>
      <ul className="space-y-2 md:hidden">
        {topics.map((t) => (
          <li key={t.topicId} className="rounded-lg border bg-card p-3">
            <div className="line-clamp-1 text-sm font-medium">{t.topicTitle}</div>
            <div className="mt-2 grid grid-cols-2 gap-x-3 gap-y-1 text-xs">
              <MiniStat label="Mastery" value={pctNum(t.avgMasteryPercent)} />
              <MiniStat label="%-верных" value={pctNum(t.avgCorrectPercent)} />
              <MiniStat label="Ответов" value={int.format(t.answersCount)} />
              <MiniStat label="Сессий" value={int.format(t.sessionsCount)} />
            </div>
          </li>
        ))}
      </ul>
    </>
  );
}

function BanksBlock({ banks }: { banks: AdminBankStat[] }) {
  if (banks.length === 0) return <EmptyRow text="Банков нет." />;
  return (
    <>
      <div className="hidden md:block">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Банк (тема)</TableHead>
              <TableHead>Назначение</TableHead>
              <TableHead className="text-right">Покрытие</TableHead>
              <TableHead className="text-right">Отвечено / всего</TableHead>
              <TableHead className="text-right">Ответов</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {banks.map((b) => (
              <TableRow key={b.bankId}>
                <TableCell className="max-w-[260px]">
                  <span className="line-clamp-1">{b.topicTitle}</span>
                  <span className="text-2xs text-muted-foreground">
                    {b.difficulty ? (DIFFICULTY_LABELS[b.difficulty] ?? b.difficulty) : "Без уровня"}
                  </span>
                </TableCell>
                <TableCell className="text-sm">{PURPOSE_LABELS[b.purpose] ?? b.purpose}</TableCell>
                <TableCell className="text-right">
                  <PercentCell value={b.coveragePercent} />
                </TableCell>
                <TableCell className="text-right tabular-nums">
                  {int.format(b.answeredQuestions)} / {int.format(b.totalQuestions)}
                </TableCell>
                <TableCell className="text-right tabular-nums">{int.format(b.answersCount)}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </div>
      <ul className="space-y-2 md:hidden">
        {banks.map((b) => (
          <li key={b.bankId} className="rounded-lg border bg-card p-3">
            <div className="flex items-center justify-between gap-2">
              <span className="line-clamp-1 text-sm font-medium">{b.topicTitle}</span>
              <span className="shrink-0 text-2xs text-muted-foreground">
                {PURPOSE_LABELS[b.purpose] ?? b.purpose}
              </span>
            </div>
            <div className="mt-2 grid grid-cols-2 gap-x-3 gap-y-1 text-xs">
              <MiniStat label="Покрытие" value={pctNum(b.coveragePercent)} />
              <MiniStat
                label="Отвечено"
                value={`${int.format(b.answeredQuestions)}/${int.format(b.totalQuestions)}`}
              />
              <MiniStat label="Ответов" value={int.format(b.answersCount)} />
              <MiniStat
                label="Уровень"
                value={b.difficulty ? (DIFFICULTY_LABELS[b.difficulty] ?? b.difficulty) : "—"}
              />
            </div>
          </li>
        ))}
      </ul>
    </>
  );
}

function CalibrationLevelCard({ level }: { level: AdminCalibrationLevel }) {
  return (
    <div className="rounded-xl border bg-card p-3">
      <div className="text-2xs font-medium tracking-wide text-muted-foreground uppercase">
        {DIFFICULTY_LABELS[level.difficulty] ?? level.difficulty}
      </div>
      <div className="mt-1 text-2xl font-semibold tabular-nums">{pctNum(level.actualCorrectPercent)}</div>
      <div className="mt-0.5 text-xs text-muted-foreground tabular-nums">
        {int.format(level.answersCount)} отв. · {int.format(level.questionsAnswered)} вопр.
      </div>
    </div>
  );
}

function MiscalibratedList({ rows }: { rows: AdminMiscalibratedQuestion[] }) {
  if (rows.length === 0) return null;
  return (
    <div className="mt-4">
      <div className="mb-2 text-2xs font-medium tracking-wide text-muted-foreground uppercase">
        Выбросы калибровки
      </div>
      <ul className="space-y-2">
        {rows.map((q) => (
          <li
            key={q.questionId}
            className="flex items-start justify-between gap-3 rounded-lg border bg-card p-3"
          >
            <div className="min-w-0">
              <div className="line-clamp-1 text-sm">{q.stem}</div>
              <div className="mt-0.5 text-2xs text-muted-foreground">
                {q.topicTitle} · {DIFFICULTY_LABELS[q.difficulty] ?? q.difficulty} ·{" "}
                {int.format(q.answersCount)} отв.
              </div>
            </div>
            <div className="shrink-0 text-right">
              <div className="text-sm font-semibold tabular-nums">{pctNum(q.actualCorrectPercent)}</div>
              <div
                className={cn(
                  "text-2xs font-medium tabular-nums",
                  q.deltaVsLevel < 0 ? "text-destructive" : "text-green",
                )}
              >
                {formatDelta(q.deltaVsLevel)}
              </div>
            </div>
          </li>
        ))}
      </ul>
    </div>
  );
}

/** Цветная пилюля %-значения по порогам (слабый/средний/сильный). */
function PercentCell({ value }: { value: number }) {
  const tone =
    value >= 80
      ? "text-green"
      : value >= 60
        ? "text-foreground"
        : "text-amber-600 dark:text-amber-400";
  return <span className={cn("font-medium tabular-nums", tone)}>{pctNum(value)}</span>;
}

function MiniStat({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex items-center justify-between gap-2">
      <span className="text-muted-foreground">{label}</span>
      <span className="font-medium tabular-nums">{value}</span>
    </div>
  );
}

/** Знаковая дельта %-пунктов (для выбросов калибровки). */
function formatDelta(d: number): string {
  const sign = d > 0 ? "+" : d < 0 ? "−" : "";
  return `${sign}${Math.abs(Math.round(d))} п.п.`;
}
