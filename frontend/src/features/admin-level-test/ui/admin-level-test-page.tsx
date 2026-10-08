"use client";

import {
  adminCrossServiceQueryOptions,
  type AdminLevelTestAttemptRow,
} from "@/entities/admin-cross-service";
import { cn } from "@/shared/lib/css";
import { DEVELOPER_LEVELS, DEVELOPER_LEVEL_LABELS, type DeveloperLevel } from "@/shared/types";
import { KpiCard } from "@/shared/ui/components";
import { Button } from "@/shared/ui/kit/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/ui/kit/card";
import { EmptyState } from "@/shared/ui/kit/empty-state";
import { Icons } from "@/shared/ui/icons";
import { Skeleton } from "@/shared/ui/kit/skeleton";
import { useQuery } from "@tanstack/react-query";
import { useState } from "react";

const numberFormatter = new Intl.NumberFormat("ru");
const dateFormatter = new Intl.DateTimeFormat("ru", { day: "2-digit", month: "2-digit" });
const dateTimeFormatter = new Intl.DateTimeFormat("ru", {
  day: "2-digit",
  month: "2-digit",
  hour: "2-digit",
  minute: "2-digit",
});

const PAGE_SIZE = 20;

function levelLabel(level: string): string {
  return DEVELOPER_LEVEL_LABELS[level as DeveloperLevel] ?? level;
}

function attemptUserLabel(row: AdminLevelTestAttemptRow): string {
  if (!row.userId) return "Аноним";
  return row.displayName || row.username || row.userId.slice(0, 8);
}

/**
 * Админ-аналитика воронки level-test (#537): KPI, распределение по 6 уровням,
 * средние по секциям (слабейшие сверху — где у аудитории пробелы), динамика
 * за 14 дней и список попыток с пагинацией «показать ещё».
 */
export function AdminLevelTestPage() {
  const overviewQuery = useQuery(adminCrossServiceQueryOptions.getLevelTestOverviewOptions());
  const [visible, setVisible] = useState(PAGE_SIZE);
  const attemptsQuery = useQuery(
    adminCrossServiceQueryOptions.getLevelTestAttemptsOptions(0, visible),
  );

  const overview = overviewQuery.data;
  const attempts = attemptsQuery.data;
  const isLoading = overviewQuery.isLoading;

  const maxLevelCount = Math.max(1, ...(overview?.levelDistribution.map((l) => l.count) ?? [0]));
  const levelCounts = new Map(overview?.levelDistribution.map((l) => [l.level, l.count]) ?? []);
  const maxDayCount = Math.max(1, ...(overview?.attemptsByDay.map((d) => d.count) ?? [0]));

  return (
    <div className="mx-auto max-w-6xl space-y-6 p-4 md:p-6">
      <div>
        <h1 className="text-2xl font-bold tracking-tight">Тест уровня</h1>
        <p className="text-sm text-muted-foreground">
          Как аудитория проходит воронку «Определи свой уровень .NET-разработчика»
        </p>
      </div>

      <div className="grid grid-cols-2 gap-4 md:grid-cols-3 lg:grid-cols-5">
        <KpiCard
          label="Всего попыток"
          value={overview ? numberFormatter.format(overview.totalAttempts) : "—"}
          isLoading={isLoading}
        />
        <KpiCard
          label="Уникальных юзеров"
          value={overview ? numberFormatter.format(overview.uniqueUsers) : "—"}
          hint="залогиненные"
          isLoading={isLoading}
        />
        <KpiCard
          label="Анонимных попыток"
          value={overview ? numberFormatter.format(overview.anonymousAttempts) : "—"}
          isLoading={isLoading}
        />
        <KpiCard
          label="За 7 дней"
          value={overview ? numberFormatter.format(overview.attemptsLast7Days) : "—"}
          isLoading={isLoading}
        />
        <KpiCard
          label="Средний результат"
          value={overview ? `${overview.averagePercent}%` : "—"}
          isLoading={isLoading}
        />
      </div>

      <div className="grid gap-4 lg:grid-cols-2">
        <Card>
          <CardHeader>
            <CardTitle className="text-base">Распределение по уровням</CardTitle>
          </CardHeader>
          <CardContent className="space-y-2.5">
            {isLoading && <Skeleton className="h-40 w-full" />}
            {overview &&
              DEVELOPER_LEVELS.map((level) => {
                const count = levelCounts.get(level) ?? 0;
                return (
                  <div key={level} className="flex items-center gap-3 text-sm">
                    <span className="w-24 shrink-0 text-muted-foreground">
                      {DEVELOPER_LEVEL_LABELS[level]}
                    </span>
                    <div className="h-2.5 flex-1 overflow-hidden rounded-full bg-border/50">
                      <div
                        className="h-full rounded-full bg-primary/70"
                        style={{ width: `${(count / maxLevelCount) * 100}%` }}
                      />
                    </div>
                    <span className="w-10 shrink-0 text-right font-mono tabular-nums">
                      {numberFormatter.format(count)}
                    </span>
                  </div>
                );
              })}
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle className="text-base">Слабые места аудитории</CardTitle>
          </CardHeader>
          <CardContent className="space-y-2.5">
            {isLoading && <Skeleton className="h-40 w-full" />}
            {overview && overview.sectionAverages.length === 0 && (
              <p className="text-sm text-muted-foreground">Пока нет данных по секциям</p>
            )}
            {overview &&
              overview.sectionAverages.map((section) => (
                <div key={section.key} className="flex items-center gap-3 text-sm">
                  <span className="w-44 shrink-0 truncate text-muted-foreground" title={section.title}>
                    {section.title}
                  </span>
                  <div className="h-2.5 flex-1 overflow-hidden rounded-full bg-border/50">
                    <div
                      className={cn(
                        "h-full rounded-full",
                        section.averagePercent < 40
                          ? "bg-rose-500/70"
                          : section.averagePercent < 65
                            ? "bg-amber-500/70"
                            : "bg-emerald-500/70",
                      )}
                      style={{ width: `${Math.min(section.averagePercent, 100)}%` }}
                    />
                  </div>
                  <span className="w-12 shrink-0 text-right font-mono tabular-nums">
                    {Math.round(section.averagePercent)}%
                  </span>
                </div>
              ))}
          </CardContent>
        </Card>
      </div>

      <Card>
        <CardHeader>
          <CardTitle className="text-base">Попытки за 14 дней</CardTitle>
        </CardHeader>
        <CardContent>
          {isLoading && <Skeleton className="h-20 w-full" />}
          {overview && overview.attemptsByDay.length === 0 && (
            <p className="text-sm text-muted-foreground">За последние две недели попыток не было</p>
          )}
          {overview && overview.attemptsByDay.length > 0 && (
            <div className="flex items-end gap-1.5">
              {overview.attemptsByDay.map((day) => (
                <div key={day.day} className="flex flex-1 flex-col items-center gap-1">
                  <span className="font-mono text-[10px] tabular-nums text-muted-foreground">
                    {day.count}
                  </span>
                  <div
                    className="w-full max-w-8 rounded-t-md bg-primary/60"
                    style={{ height: `${8 + (day.count / maxDayCount) * 56}px` }}
                  />
                  <span className="text-[10px] text-muted-foreground">
                    {dateFormatter.format(new Date(day.day))}
                  </span>
                </div>
              ))}
            </div>
          )}
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="text-base">
            Последние попытки
            {attempts && (
              <span className="ml-2 font-normal text-muted-foreground">
                {numberFormatter.format(attempts.totalCount)} всего
              </span>
            )}
          </CardTitle>
        </CardHeader>
        <CardContent className="space-y-3">
          {attemptsQuery.isLoading && <Skeleton className="h-48 w-full" />}
          {attempts && attempts.items.length === 0 && (
            <EmptyState
              variant="plain"
              icon={Icons.searchEmpty}
              title="Попыток пока нет"
              description="Как только кто-то пройдёт тест — попытка появится здесь."
            />
          )}

          {attempts && attempts.items.length > 0 && (
            <>
              {/* Desktop-таблица */}
              <div className="hidden md:block">
                <table className="w-full text-sm">
                  <thead>
                    <tr className="border-b border-border/60 text-left text-xs text-muted-foreground">
                      <th className="pb-2 pr-3 font-medium">Кто</th>
                      <th className="pb-2 pr-3 font-medium">Уровень</th>
                      <th className="pb-2 pr-3 font-medium">Результат</th>
                      <th className="pb-2 pr-3 font-medium">Отвечено</th>
                      <th className="pb-2 pr-3 font-medium">Когда</th>
                    </tr>
                  </thead>
                  <tbody>
                    {attempts.items.map((row) => (
                      <tr key={row.attemptId} className="border-b border-border/40">
                        <td className="py-2 pr-3">
                          <span className={cn(!row.userId && "text-muted-foreground")}>
                            {attemptUserLabel(row)}
                          </span>
                          {row.userId && row.username && row.displayName && (
                            <span className="ml-1.5 text-xs text-muted-foreground">
                              @{row.username}
                            </span>
                          )}
                        </td>
                        <td className="py-2 pr-3">{levelLabel(row.level)}</td>
                        <td className="py-2 pr-3 font-mono tabular-nums">{row.overallPercent}%</td>
                        <td className="py-2 pr-3 font-mono tabular-nums text-muted-foreground">
                          {row.answeredCount}/{row.totalQuestions}
                        </td>
                        <td className="py-2 pr-3 text-muted-foreground">
                          {dateTimeFormatter.format(new Date(row.createdAt))}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>

              {/* Mobile-карточки */}
              <div className="space-y-2 md:hidden">
                {attempts.items.map((row) => (
                  <div
                    key={row.attemptId}
                    className="rounded-lg border border-border/60 bg-card/50 p-3 text-sm"
                  >
                    <div className="flex items-center justify-between gap-2">
                      <span className={cn("font-medium", !row.userId && "text-muted-foreground")}>
                        {attemptUserLabel(row)}
                      </span>
                      <span className="font-mono tabular-nums">{row.overallPercent}%</span>
                    </div>
                    <div className="mt-1 flex items-center justify-between gap-2 text-xs text-muted-foreground">
                      <span>
                        {levelLabel(row.level)} · {row.answeredCount}/{row.totalQuestions} ответов
                      </span>
                      <span>{dateTimeFormatter.format(new Date(row.createdAt))}</span>
                    </div>
                  </div>
                ))}
              </div>

              {attempts.items.length < attempts.totalCount && (
                <Button
                  variant="outline"
                  className="w-full"
                  disabled={attemptsQuery.isFetching}
                  onClick={() => setVisible((v) => v + PAGE_SIZE)}
                >
                  {attemptsQuery.isFetching ? (
                    <Icons.loading className="size-4 animate-spin" />
                  ) : (
                    <Icons.chevronDown className="size-4" />
                  )}
                  Показать ещё
                </Button>
              )}
            </>
          )}
        </CardContent>
      </Card>
    </div>
  );
}
