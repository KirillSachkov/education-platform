"use client";

import { myOrdersQueryOptions } from "@/entities/access-order";
import {
  formatPriceFromCents,
  isActiveTrialGrant,
  myGrantsQueryOptions,
  PLAN_CAPABILITY_LABELS,
  type PlanCapability,
  type PlanGrantDto,
  type PlanGrantSource,
  type PlanSummaryDto,
} from "@/entities/access-plan";
import { planOnboardingApi } from "@/entities/plan-onboarding";
import { getErrorMessage } from "@/shared/api";
import { routes } from "@/shared/config/routes";
import { Badge } from "@/shared/ui/kit/badge";
import { Button } from "@/shared/ui/kit/button";
import { EmptyState } from "@/shared/ui/kit/empty-state";
import { Icons } from "@/shared/ui/icons";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { toast } from "sonner";
import {
  hasSubscriptionLifecycle,
  SubscriptionRenewalControls,
} from "./subscription-renewal-controls";
import { TrialAccessBanner } from "./trial-access-banner";

const SOURCE_LABEL: Record<PlanGrantSource, string> = {
  INVITE_LINK: "По ссылке-приглашению",
  ADMIN_GRANT: "Выдано администратором",
  MIGRATION: "Перенос данных",
  PURCHASE: "Покупка",
  TRIAL: "Бесплатный план",
  GITHUB_ORG: "GitHub-организация",
  TELEGRAM_F1: "Telegram",
  AUTO_FREE: "Автоматически",
};

/**
 * `/my-plans` — полный обзор активных доступов пользователя: план, источник,
 * дата выдачи, срок, оплаченная сумма, возможности (capabilities) и какие курсы
 * покрывает. Промоут из `/settings/plans` (#414).
 */
export function MyPlansView() {
  const grantsQuery = useQuery(myGrantsQueryOptions());
  // Оплаченную сумму берём из PAID-заказа того же плана (best-effort: grant DTO
  // не несёт price_paid). Для grant'ов без покупки (invite/github/trial) суммы нет.
  const ordersQuery = useQuery(myOrdersQueryOptions({ status: "PAID", pageSize: 50 }));

  const paidAmountByPlanId = new Map<string, { amountCents: number; currency: string }>();
  for (const order of ordersQuery.data?.items ?? []) {
    if (!paidAmountByPlanId.has(order.planId)) {
      paidAmountByPlanId.set(order.planId, {
        amountCents: order.amountCents,
        currency: order.currency,
      });
    }
  }

  const grants: PlanGrantDto[] = grantsQuery.data ?? [];
  const activeGrants = grants.filter((g) => g.status === "ACTIVE");
  const hasPermanentFullAccess = activeGrants.some(
    (g) => g.expiresAt == null && g.plan?.tier === "FULL_ALL",
  );
  // Paid trial остаётся ACTIVE до своего срока для истории оплаты. После upgrade
  // скрываем только этот дублирующий FULL_ALL, не другие временные доступы.
  const effectiveGrants = hasPermanentFullAccess
    ? activeGrants.filter((g) => !(isActiveTrialGrant(g) && g.plan?.tier === "FULL_ALL"))
    : activeGrants;

  // Пробный доступ (#580): один баннер на все активные trial-grant'ы, по самому
  // позднему сроку — если их несколько, юзеру важна крайняя дата, до которой
  // доступ открыт.
  const trialExpiresAt = effectiveGrants
    .filter(isActiveTrialGrant)
    .map((g) => g.expiresAt!)
    .sort()
    .at(-1);

  return (
    <div className="mx-auto w-full max-w-3xl px-4 sm:px-6 py-6 sm:py-10">
      <header className="mb-6 sm:mb-8 space-y-1.5">
        <h1 className="text-2xl sm:text-3xl font-semibold tracking-tight">Мои планы</h1>
        <p className="text-sm text-muted-foreground">
          Активные доступы к платным планам: что входит, какие курсы покрыты и срок действия.
        </p>
      </header>

      {grantsQuery.isLoading ? (
        <div className="space-y-3">
          {[1, 2].map((i) => (
            <div key={i} className="h-40 rounded-2xl bg-muted/40 animate-pulse" />
          ))}
        </div>
      ) : grantsQuery.isError ? (
        <EmptyState
          variant="card"
          icon={Icons.error}
          title="Не удалось загрузить планы"
          description="Перезагрузите страницу или зайдите позже."
        />
      ) : activeGrants.length === 0 ? (
        <EmptyState
          variant="card"
          icon={Icons.crown}
          title="Активных планов нет"
          description="Выберите тариф, чтобы открыть доступ к материалам, заданиям и сообществу."
          action={
            <Button asChild>
              <Link href={routes.pricing}>Посмотреть планы</Link>
            </Button>
          }
        />
      ) : (
        <div>
          {trialExpiresAt ? <TrialAccessBanner expiresAt={trialExpiresAt} /> : null}
          <div className="space-y-4">
            {effectiveGrants.map((grant) => (
              <PlanGrantCard
                key={grant.id}
                grant={grant}
                paidAmount={paidAmountByPlanId.get(grant.planId) ?? null}
              />
            ))}
          </div>
        </div>
      )}
    </div>
  );
}

function PlanGrantCard({
  grant,
  paidAmount,
}: {
  grant: PlanGrantDto;
  paidAmount: { amountCents: number; currency: string } | null;
}) {
  const plan = grant.plan;
  const visibleCapabilities = (plan?.capabilities ?? []).filter(
    (cap) => !HIDDEN_CAPABILITIES.has(cap),
  );
  const title = plan?.displayName ?? "План";
  const subtitle = plan?.shortDescription ?? null;

  return (
    <article className="rounded-2xl border border-border/60 bg-card/60 p-4 sm:p-5">
      <header className="flex items-start gap-3">
        <span className="flex size-10 shrink-0 items-center justify-center rounded-xl bg-primary/10 text-primary">
          <Icons.crown className="size-5" />
        </span>
        <div className="min-w-0 flex-1">
          <div className="flex flex-wrap items-center gap-2">
            <h2 className="text-base font-semibold leading-tight">{title}</h2>
            <Badge variant="secondary" className="font-normal">
              {SOURCE_LABEL[grant.source] ?? grant.source}
            </Badge>
          </div>
          {subtitle ? <p className="mt-1 text-sm text-muted-foreground">{subtitle}</p> : null}
        </div>
      </header>

      <dl className="mt-4 grid grid-cols-1 gap-x-6 gap-y-2 text-sm sm:grid-cols-2">
        <Row label="Выдан" value={formatDate(grant.grantedAt)} />
        <Row
          label="Срок"
          value={grant.expiresAt ? `до ${formatDate(grant.expiresAt)}` : "Бессрочно"}
        />
        {paidAmount ? (
          <Row
            label="Оплачено"
            value={formatPriceFromCents(paidAmount.amountCents, paidAmount.currency)}
          />
        ) : null}
        {plan ? <Row label="Покрывает" value={coverageLabel(plan)} /> : null}
      </dl>

      {hasSubscriptionLifecycle(grant) ? <SubscriptionRenewalControls grant={grant} /> : null}

      {visibleCapabilities.length > 0 ? (
        <div className="mt-4">
          <p className="mb-2 text-xs font-medium uppercase tracking-wide text-muted-foreground">
            Возможности
          </p>
          <ul className="grid grid-cols-1 gap-1.5 sm:grid-cols-2">
            {visibleCapabilities.map((cap) => (
              <li key={cap} className="flex items-center gap-2 text-sm">
                <Icons.check className="size-3.5 shrink-0 text-emerald-500" />
                <span className="text-foreground/90">{capabilityLabel(cap)}</span>
              </li>
            ))}
          </ul>
        </div>
      ) : null}

      {plan?.hasOnboardingEnabled ? <RestartOnboardingButton planId={plan.id} /> : null}
    </article>
  );
}

/**
 * Какие курсы покрывает план. Grant-summary не несёт полного списка курсов, но
 * tier + `includesFutureContent` дают точную семантику покрытия без второго
 * round-trip'а: FULL/LEARN-тиры → все курсы платформы, COURSE → конкретный курс/подборка.
 */
function coverageLabel(plan: PlanSummaryDto): string {
  if (plan.tier === "LEARN_ALL" || plan.tier === "FULL_ALL") {
    return plan.includesFutureContent
      ? "Все курсы платформы, включая будущие"
      : "Все курсы платформы";
  }
  if (plan.tier === "COURSE") {
    return plan.includesFutureContent
      ? "Курсы плана, включая будущие материалы"
      : "Курсы, входящие в план";
  }
  return plan.includesFutureContent ? "Контент плана, включая будущий" : "Контент плана";
}

function RestartOnboardingButton({ planId }: { planId: string }) {
  const queryClient = useQueryClient();
  const reset = useMutation({
    mutationFn: () => planOnboardingApi.resetOnboarding(planId),
    onSuccess: async () => {
      // Await обязателен: без него toast и закрытие mutation'а опередят refetch
      // (regression-pattern #208/#233).
      await queryClient.invalidateQueries({ queryKey: ["plan-onboarding", "current"] });
      toast.success("Онбординг сброшен — открываем wizard");
    },
    onError: (error) => {
      const code = (
        error as { response?: { data?: { error?: { messages?: { code?: string }[] } } } }
      )?.response?.data?.error?.messages?.[0]?.code;
      if (code === "onboarding.not.found") {
        toast.info("Для этого плана онбординг не запущен");
        return;
      }
      if (code === "onboarding.flow.disabled") {
        toast.info("Автор выключил онбординг этого плана");
        return;
      }
      toast.error(getErrorMessage(error, "Не удалось сбросить онбординг"));
    },
  });

  return (
    <div className="mt-4 border-t border-border/40 pt-3">
      <Button size="sm" variant="ghost" disabled={reset.isPending} onClick={() => reset.mutate()}>
        <Icons.refresh className="size-4" />
        {reset.isPending ? "Сбрасываем…" : "Пройти онбординг заново"}
      </Button>
    </div>
  );
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex items-baseline justify-between gap-3 sm:block">
      <dt className="text-xs text-muted-foreground">{label}</dt>
      <dd className="text-sm text-foreground/90">{value}</dd>
    </div>
  );
}

/**
 * Флаги плана, которые не показываем владельцу: услуга по ним больше не оказывается
 * (offer-v2, #1160), а менять capabilities существующих планов нельзя — это пересчитывает доступ.
 */
const HIDDEN_CAPABILITIES: ReadonlySet<string> = new Set<PlanCapability>(["LIVE_CALLS"]);

function capabilityLabel(cap: PlanCapability | string): string {
  return PLAN_CAPABILITY_LABELS[cap as PlanCapability] ?? cap;
}

function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString("ru-RU", {
    day: "numeric",
    month: "short",
    year: "numeric",
  });
}
