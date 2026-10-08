"use client";

import { useQuery } from "@tanstack/react-query";

import {
  type AdminAiModelBreakdown,
  type AdminAiOperationBreakdown,
  type AdminAiTopUser,
  type AdminUsage,
  adminStatsQueryOptions,
  type TrainerAdminStatsRange,
} from "@/entities/trainer-admin-stats";
import { trainerProRevenueQueryOptions, type TrainerProRevenue } from "@/entities/access-plan";
import { routes } from "@/shared/config/routes";
import { cn } from "@/shared/lib/css";
import { Icons } from "@/shared/ui/icons";
import { Avatar, AvatarFallback, AvatarImage } from "@/shared/ui/kit/avatar";
import { Skeleton } from "@/shared/ui/kit/skeleton";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/shared/ui/kit/table";

import { DailyCostChart } from "./daily-cost-chart";
import {
  AdminStatCard,
  AdminStatSection,
  EmptyRow,
  int,
  MODE_LABELS,
  rub,
  StatsErrorState,
  TabSkeleton,
  toRub,
} from "./primitives";

/** Деньги-вкладка (#681/#680): выручка Trainer Pro + AI-расходы, стоимость на единицу, разбивки, топ по тратам. */
export function MoneyTab({ days }: { days: TrainerAdminStatsRange }) {
  const { data, isLoading, error } = useQuery(adminStatsQueryOptions(days));

  return (
    <div className="space-y-4">
      <TrainerProRevenueSection days={days} />

      {error && <StatsErrorState error={error} />}
      {isLoading && !error && <TabSkeleton />}

      {data && !error && (
        <>
          <section className="grid grid-cols-2 gap-3 md:grid-cols-4">
            <AdminStatCard
              label="AI-расходы, ₽"
              value={data.aiSpend.totalCostRub}
              format={(n) => rub.format(n)}
              decimals={2}
              icon={<Icons.creditCard className="size-4" />}
              index={0}
            />
            <AdminStatCard
              label="AI-операций"
              value={data.aiSpend.totalOperations}
              icon={<Icons.ai className="size-4" />}
              index={1}
            />
            <AdminStatCard
              label="Входных токенов"
              value={data.aiSpend.totalInputTokens}
              icon={<Icons.arrowRight className="size-4" />}
              index={2}
            />
            <AdminStatCard
              label="Выходных токенов"
              value={data.aiSpend.totalOutputTokens}
              icon={<Icons.arrowLeft className="size-4" />}
              index={3}
            />
          </section>

          <AdminStatSection
            title="Стоимость на единицу"
            icon={<Icons.percent className="size-4" />}
            hint="Средняя AI-стоимость на пользователя / сессию / проверку открытого ответа + проекция на 30 дней."
          >
            <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
              <AdminStatCard
                label="На пользователя, ₽"
                value={data.aiSpend.money.costPerUserRub}
                format={(n) => rub.format(n)}
                decimals={2}
                index={0}
              />
              <AdminStatCard
                label="На сессию, ₽"
                value={data.aiSpend.money.costPerSessionRub}
                format={(n) => rub.format(n)}
                decimals={2}
                index={1}
              />
              <AdminStatCard
                label="На проверку, ₽"
                value={data.aiSpend.money.costPerGradeRub}
                format={(n) => rub.format(n)}
                decimals={2}
                index={2}
              />
              <AdminStatCard
                label="Проекция/мес, ₽"
                value={data.aiSpend.money.projectedMonthRub}
                format={(n) => rub.format(n)}
                decimals={2}
                accentClass="text-amber-500"
                icon={<Icons.trending className="size-4" />}
                index={3}
              />
            </div>
          </AdminStatSection>

          <AdminStatSection title="Стоимость AI по дням, ₽" icon={<Icons.chart className="size-4" />}>
            <DailyCostChart daily={data.aiSpend.daily} />
          </AdminStatSection>

          <div className="grid gap-4 lg:grid-cols-2">
            <OperationBreakdownCard rows={data.aiSpend.byOperation} />
            <ModelBreakdownCard rows={data.aiSpend.byModel} />
          </div>

          <TopUsersCard rows={data.aiSpend.topUsers} />

          <UsageSection usage={data.usage} />
        </>
      )}
    </div>
  );
}

// ─────────────────────────────────────────────────────────────────────────────

/**
 * Выручка + подписки Trainer Pro (#623): собственный запрос к кросс-плановому агрегату
 * AccessService. Опциональна — при ошибке/нет прав секция не рендерится, дашборд не падает.
 */
function TrainerProRevenueSection({ days }: { days: TrainerAdminStatsRange }) {
  const { data, isLoading, error } = useQuery(trainerProRevenueQueryOptions(days));

  if (error) return null;

  return (
    <AdminStatSection title="Trainer Pro — подписки и выручка" icon={<Icons.trophy className="size-4" />}>
      {isLoading || !data ? (
        <Skeleton className="h-20 w-full rounded-lg" />
      ) : (
        <RevenueBody data={data} days={days} />
      )}
    </AdminStatSection>
  );
}

function RevenueBody({ data, days }: { data: TrainerProRevenue; days: TrainerAdminStatsRange }) {
  // newInPeriod несёт только 7/30/90-дневные бакеты — для 365 берём ближайший (90) и честно
  // подписываем окно, чтобы счётчик не врал.
  const window = days >= 90 ? 90 : days >= 30 ? 30 : 7;
  const newInPeriod =
    window === 7
      ? data.newInPeriod.last7Days
      : window === 30
        ? data.newInPeriod.last30Days
        : data.newInPeriod.last90Days;
  const mrrRub = Math.round(data.mrrCentsEstimate / 100);

  return (
    <div className="space-y-4">
      <section className="grid grid-cols-2 gap-3 md:grid-cols-4">
        <AdminStatCard label="Активные подписки" value={data.activeSubscriptions} index={0} />
        <AdminStatCard label="MRR (оценка), ₽" value={mrrRub} index={1} />
        <AdminStatCard label={`Новые за ${window} дн.`} value={newInPeriod} index={2} />
        <AdminStatCard label="Отписки (всего)" value={data.canceledSubscriptions} index={3} />
      </section>

      {data.totalGrants === 0 ? (
        <p className="text-sm text-muted-foreground">
          Пока нет плана Trainer Pro или выдач. Создай план с offer-type «Тренажёр» в управлении
          планами — метрики появятся здесь.
        </p>
      ) : (
        data.sourceBreakdown.length > 0 && (
          <div className="flex flex-wrap gap-2">
            {data.sourceBreakdown.map((s) => (
              <span
                key={s.source}
                className="inline-flex items-center gap-1.5 rounded-md bg-muted px-2 py-1 text-xs"
                title="Источник выдачи подписки"
              >
                <span className="text-muted-foreground">{s.source}</span>
                <span className="font-medium tabular-nums">{int.format(s.count)}</span>
              </span>
            ))}
          </div>
        )
      )}
    </div>
  );
}

function OperationBreakdownCard({ rows }: { rows: AdminAiOperationBreakdown[] }) {
  return (
    <AdminStatSection title="По операциям" icon={<Icons.listChecks className="size-4" />}>
      {rows.length === 0 ? (
        <EmptyRow text="Нет AI-операций за период." />
      ) : (
        <>
          <div className="hidden md:block">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Операция</TableHead>
                  <TableHead className="text-right">Вызовов</TableHead>
                  <TableHead className="text-right">₽</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {rows.map((row) => (
                  <TableRow key={row.operation}>
                    <TableCell className="font-mono text-xs">{row.operation}</TableCell>
                    <TableCell className="text-right tabular-nums">{int.format(row.count)}</TableCell>
                    <TableCell className="text-right tabular-nums">
                      {rub.format(toRub(row.costMicroRub))}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
          <ul className="space-y-2 md:hidden">
            {rows.map((row) => (
              <li key={row.operation} className="rounded-lg border bg-card p-3">
                <div className="font-mono text-xs break-all">{row.operation}</div>
                <div className="mt-1 flex justify-between text-sm">
                  <span className="text-muted-foreground">{int.format(row.count)} вызовов</span>
                  <span className="font-medium tabular-nums">
                    {rub.format(toRub(row.costMicroRub))} ₽
                  </span>
                </div>
              </li>
            ))}
          </ul>
        </>
      )}
    </AdminStatSection>
  );
}

function ModelBreakdownCard({ rows }: { rows: AdminAiModelBreakdown[] }) {
  return (
    <AdminStatSection title="По моделям" icon={<Icons.ai className="size-4" />}>
      {rows.length === 0 ? (
        <EmptyRow text="Нет AI-вызовов за период." />
      ) : (
        <>
          <div className="hidden md:block">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Модель</TableHead>
                  <TableHead className="text-right">Вызовов</TableHead>
                  <TableHead className="text-right">Токены (in/out)</TableHead>
                  <TableHead className="text-right">₽</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {rows.map((row) => (
                  <TableRow key={row.model}>
                    <TableCell className="font-mono text-xs">{row.model}</TableCell>
                    <TableCell className="text-right tabular-nums">{int.format(row.count)}</TableCell>
                    <TableCell className="text-right tabular-nums text-xs">
                      {int.format(row.inputTokens)} / {int.format(row.outputTokens)}
                    </TableCell>
                    <TableCell className="text-right tabular-nums">
                      {rub.format(toRub(row.costMicroRub))}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
          <ul className="space-y-2 md:hidden">
            {rows.map((row) => (
              <li key={row.model} className="rounded-lg border bg-card p-3">
                <div className="font-mono text-xs break-all">{row.model}</div>
                <div className="mt-1 flex justify-between text-sm">
                  <span className="text-muted-foreground">{int.format(row.count)} вызовов</span>
                  <span className="font-medium tabular-nums">
                    {rub.format(toRub(row.costMicroRub))} ₽
                  </span>
                </div>
                <div className="mt-0.5 text-xs text-muted-foreground tabular-nums">
                  Токены: {int.format(row.inputTokens)} вх. / {int.format(row.outputTokens)} вых.
                </div>
              </li>
            ))}
          </ul>
        </>
      )}
    </AdminStatSection>
  );
}

/** Топ по тратам (#680): имя + аватар вместо GUID, fallback на короткий id. */
function TopUsersCard({ rows }: { rows: AdminAiTopUser[] }) {
  return (
    <AdminStatSection title="Топ пользователей по тратам" icon={<Icons.users className="size-4" />}>
      {rows.length === 0 ? (
        <EmptyRow text="Нет трат за период." />
      ) : (
        <>
          <div className="hidden md:block">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Пользователь</TableHead>
                  <TableHead className="text-right">Операций</TableHead>
                  <TableHead className="text-right">₽</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {rows.map((row) => (
                  <TableRow key={row.userId}>
                    <TableCell>
                      <UserCredit row={row} />
                    </TableCell>
                    <TableCell className="text-right tabular-nums">
                      {int.format(row.operationCount)}
                    </TableCell>
                    <TableCell className="text-right tabular-nums">
                      {rub.format(toRub(row.costMicroRub))}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
          <ul className="space-y-2 md:hidden">
            {rows.map((row) => (
              <li key={row.userId} className="rounded-lg border bg-card p-3">
                <UserCredit row={row} />
                <div className="mt-2 flex justify-between text-sm">
                  <span className="text-muted-foreground">
                    {int.format(row.operationCount)} операций
                  </span>
                  <span className="font-medium tabular-nums">
                    {rub.format(toRub(row.costMicroRub))} ₽
                  </span>
                </div>
              </li>
            ))}
          </ul>
        </>
      )}
    </AdminStatSection>
  );
}

/** Имя + аватар топ-юзера (#680). Имя null → короткий id; аватар null → инициалы. */
function UserCredit({ row }: { row: AdminAiTopUser }) {
  const shortId = `${row.userId.slice(0, 8)}…`;
  const name = row.displayName ?? shortId;
  const initials = row.displayName ? deriveInitials(row.displayName) : "?";

  return (
    <a
      href={routes.adminUserDetail(row.userId)}
      className="inline-flex items-center gap-2 text-sm hover:underline"
      title={row.userId}
    >
      <Avatar className="size-7">
        {row.avatarUrl ? (
          <AvatarImage src={row.avatarUrl} sizes="56px" alt={name} />
        ) : null}
        <AvatarFallback className="bg-gradient-primary text-2xs font-bold text-primary-foreground">
          {initials}
        </AvatarFallback>
      </Avatar>
      <span className={cn("truncate", !row.displayName && "font-mono text-xs text-muted-foreground")}>
        {name}
      </span>
    </a>
  );
}

function deriveInitials(name: string): string {
  return (
    name
      .split(/\s+/)
      .filter(Boolean)
      .map((w) => w[0])
      .join("")
      .toUpperCase()
      .slice(0, 2) || "?"
  );
}

function UsageSection({ usage }: { usage: AdminUsage }) {
  return (
    <AdminStatSection title="Активность за окно" icon={<Icons.calendar className="size-4" />}>
      <div className="grid grid-cols-2 gap-3 md:grid-cols-3">
        <AdminStatCard label="Сессий начато" value={usage.sessionsStarted} index={0} />
        <AdminStatCard label="Активных юзеров" value={usage.activeUsers} index={1} />
        <AdminStatCard label="Завершено сессий" value={usage.completedSessions} index={2} />
      </div>
      <div className="mt-4">
        <div className="mb-2 text-2xs font-medium tracking-wide text-muted-foreground uppercase">
          По режимам
        </div>
        {usage.byMode.length === 0 ? (
          <EmptyRow text="Сессий за период не было." />
        ) : (
          <div className="flex flex-wrap gap-2">
            {usage.byMode.map((m) => (
              <span
                key={m.mode}
                className="inline-flex items-center gap-1.5 rounded-lg border bg-card px-3 py-1.5 text-sm"
              >
                <span className="font-medium">{MODE_LABELS[m.mode] ?? m.mode}</span>
                <span className="tabular-nums text-muted-foreground">{int.format(m.count)}</span>
              </span>
            ))}
          </div>
        )}
      </div>
    </AdminStatSection>
  );
}
