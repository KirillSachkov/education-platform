"use client";

import { myPlansQueryOptions, type PlanDto, type PlanTier } from "@/entities/access-plan";
import { ROLES, useRoles } from "@/shared/auth";
import { routes } from "@/shared/config/routes";
import { cn } from "@/shared/lib/css";
import { Badge } from "@/shared/ui/kit/badge";
import { Button } from "@/shared/ui/kit/button";
import { Card } from "@/shared/ui/kit/card";
import { Icons } from "@/shared/ui/icons";
import { EmptyState } from "@/shared/ui/kit/empty-state";
import { DragDropProvider } from "@dnd-kit/react";
import { isSortable, useSortable } from "@dnd-kit/react/sortable";
import { useQuery } from "@tanstack/react-query";
import { GripVertical } from "lucide-react";
import Link from "next/link";
import { useReorderPlans } from "../model/use-author-plans";

const TIER_META: Record<PlanTier, { label: string; icon: keyof typeof Icons }> = {
  FULL_ALL: { label: "Полный доступ", icon: "crown" },
  LEARN_ALL: { label: "Все материалы", icon: "library" },
  COURSE: { label: "Подборка курсов", icon: "grid" },
  SUBSCRIPTION: { label: "Подписка", icon: "rocket" },
  FREE: { label: "Бесплатный", icon: "gift" },
};

/** Trial-план («Пробный доступ») детектим по ненулевому сроку (#595). */
function isTrialPlanDto(plan: PlanDto): boolean {
  return (plan.trialDurationDays ?? 0) > 0;
}

export function AuthorPlansList() {
  const plansQuery = useQuery(myPlansQueryOptions());
  const reorder = useReorderPlans();
  const { hasAnyRole } = useRoles();
  const canViewPlatformPlans = hasAnyRole([ROLES.ADMIN, ROLES.OWNER]);
  // Trial — системный singleton на платформу (#595/#604): показываем отдельным
  // read-only слотом и не смешиваем с обычными редактируемыми планами.
  const plans = plansQuery.data ?? [];
  const trialPlan = plans.find(isTrialPlanDto);
  const regularPlans = plans.filter((plan) => !isTrialPlanDto(plan));
  const title = canViewPlatformPlans ? "Планы платформы" : "Мои планы доступа";
  const description = canViewPlatformPlans
    ? "Управляйте планами всех авторов платформы. В карточках указан владелец плана."
    : "Управляйте своими пакетами доступа: пригласительные ссылки, продажи и админ-выдача.";

  const handleDragEnd = (event: Parameters<
    NonNullable<React.ComponentProps<typeof DragDropProvider>["onDragEnd"]>
  >[0]) => {
    if (event.canceled) return;
    const { source } = event.operation;
    if (!isSortable(source)) return;
    const { initialIndex, index } = source;
    if (initialIndex === index) return;
    if (regularPlans.length === 0) return;

    const reordered = [...regularPlans];
    const [moved] = reordered.splice(initialIndex, 1);
    reordered.splice(index, 0, moved);
    reorder.mutate(
      reordered.map((plan, idx) => ({ planId: plan.id, displayOrder: idx })),
    );
  };

  // Touch-friendly reorder fallback (drag is janky on touch). Moves a plan one
  // slot up/down via the SAME splice + full-order mutation as drag-end.
  const moveByOffset = (index: number, direction: "up" | "down") => {
    if (regularPlans.length === 0) return;
    const newIndex = direction === "up" ? index - 1 : index + 1;
    if (newIndex < 0 || newIndex >= regularPlans.length) return;

    const reordered = [...regularPlans];
    const [moved] = reordered.splice(index, 1);
    reordered.splice(newIndex, 0, moved);
    reorder.mutate(
      reordered.map((plan, idx) => ({ planId: plan.id, displayOrder: idx })),
    );
  };

  return (
    <div className="mx-auto mt-8 max-w-5xl space-y-6 px-4">
      <header className="flex flex-col gap-3 sm:flex-row sm:items-end sm:justify-between">
        <div className="space-y-1">
          <h1 className="text-2xl font-semibold tracking-tight">{title}</h1>
          <p className="text-sm text-muted-foreground">{description}</p>
        </div>
        <Button asChild className="self-start sm:self-auto">
          <Link href={routes.authorPlanCreate}>
            <Icons.add className="size-4" />
            Новый план
          </Link>
        </Button>
      </header>

      {/* Singleton-слот «Пробный доступ» (#595/#604) — рядом с полным доступом.
          Есть trial-план → read-only карточка; нет → CTA создания.
          Ждём загрузку, чтобы не мигать CTA при уже существующем trial'е. */}
      {!plansQuery.isLoading ? (
        <TrialPlanSlot trialPlan={trialPlan} showOwner={canViewPlatformPlans} />
      ) : null}

      {plansQuery.isLoading ? (
        <div className="space-y-4">
          {[1, 2, 3, 4].map((i) => (
            <div key={i} className="h-32 rounded-xl bg-muted/40 animate-pulse" />
          ))}
        </div>
      ) : regularPlans.length === 0 ? (
        <EmptyState
          variant="dashed"
          icon={Icons.crown}
          title="Пока нет обычных планов"
          description="Создайте первый план — он определит, к какому контенту ученики получат доступ через invite-ссылки или бесплатно."
          action={
            <Button asChild>
              <Link href={routes.authorPlanCreate}>
                <Icons.add className="size-4" />
                Создать план
              </Link>
            </Button>
          }
        />
      ) : (
        <DragDropProvider onDragEnd={handleDragEnd}>
          <div className="space-y-4">
            {regularPlans.map((plan, idx) => (
              <SortablePlanCard
                key={plan.id}
                plan={plan}
                index={idx}
                canMoveUp={idx > 0}
                canMoveDown={idx < regularPlans.length - 1}
                onMoveUp={() => moveByOffset(idx, "up")}
                onMoveDown={() => moveByOffset(idx, "down")}
                showOwner={canViewPlatformPlans}
              />
            ))}
          </div>
        </DragDropProvider>
      )}
    </div>
  );
}

/**
 * TrialPlanSlot (#595/#604) — фиксированный слот «Пробного доступа». Есть trial-план →
 * read-only карточка с названием/статусом/ценой; нет → CTA создания.
 * Это singleton: повторное создание отвергнет backend (409 plan.trial.duplicate).
 */
function TrialPlanSlot({
  trialPlan,
  showOwner,
}: {
  trialPlan: PlanDto | undefined;
  showOwner: boolean;
}) {
  if (trialPlan) {
    return (
      <Card className="border-primary/30 bg-primary/[0.03] p-5">
        <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
          <div className="flex min-w-0 items-start gap-3">
            <div className="flex size-10 shrink-0 items-center justify-center rounded-lg bg-primary/10 text-primary">
              <Icons.clock className="size-5" />
            </div>
            <div className="min-w-0 space-y-1.5">
              <div className="flex flex-wrap items-center gap-2">
                <h3 className="truncate text-base font-semibold leading-tight">
                  {trialPlan.displayName}
                </h3>
                <Badge variant="outline" className="text-[11px] font-normal">
                  Пробный доступ
                </Badge>
                {trialPlan.isPublic ? (
                  <Badge className="bg-emerald-500/15 text-emerald-600 hover:bg-emerald-500/20 text-[11px] font-normal dark:text-emerald-400">
                    Опубликован
                  </Badge>
                ) : (
                  <Badge variant="secondary" className="text-[11px] font-normal">
                    Черновик
                  </Badge>
                )}
                {showOwner ? (
                  <Badge variant="outline" className="text-[11px] font-normal text-muted-foreground">
                    Автор: {shortId(trialPlan.authorId)}
                  </Badge>
                ) : null}
              </div>
              <p className="text-xs text-muted-foreground">
                Полный доступ на месяц
                {trialPlan.priceCents
                  ? ` · ${(trialPlan.priceCents / 100).toLocaleString("ru")} ${trialPlan.currency}`
                  : ""}
              </p>
            </div>
          </div>
          <Button asChild variant="outline" size="sm" className="self-start sm:self-auto">
            <Link href={routes.authorPlanDetail(trialPlan.id)}>Открыть</Link>
          </Button>
        </div>
      </Card>
    );
  }

  return (
    <Card className="border-dashed border-primary/30 bg-primary/[0.02] p-5">
      <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <div className="flex items-start gap-3">
          <div className="flex size-10 shrink-0 items-center justify-center rounded-lg bg-primary/10 text-primary">
            <Icons.clock className="size-5" />
          </div>
          <div className="space-y-0.5">
            <h3 className="text-base font-semibold leading-tight">Пробный доступ</h3>
            <p className="text-sm text-muted-foreground">
              Полный доступ к обучению .NET Fullstack на месяц — потом доплата до полного,
              в зачёт.
            </p>
          </div>
        </div>
        <Button asChild size="sm" className="self-start sm:self-auto">
          <Link href={routes.authorTrialPlanCreate}>
            <Icons.add className="size-4" />
            Создать пробный доступ
          </Link>
        </Button>
      </div>
    </Card>
  );
}

function SortablePlanCard({
  plan,
  index,
  canMoveUp,
  canMoveDown,
  onMoveUp,
  onMoveDown,
  showOwner,
}: {
  plan: PlanDto;
  index: number;
  canMoveUp: boolean;
  canMoveDown: boolean;
  onMoveUp: () => void;
  onMoveDown: () => void;
  showOwner: boolean;
}) {
  const { ref, handleRef, isDragging } = useSortable({ id: plan.id, index });
  return (
    <div ref={ref} className={isDragging ? "opacity-50" : undefined}>
      <PlanCard
        plan={plan}
        handleRef={handleRef}
        canMoveUp={canMoveUp}
        canMoveDown={canMoveDown}
        onMoveUp={onMoveUp}
        onMoveDown={onMoveDown}
        showOwner={showOwner}
      />
    </div>
  );
}

function PlanCard({
  plan,
  handleRef,
  canMoveUp,
  canMoveDown,
  onMoveUp,
  onMoveDown,
  showOwner,
}: {
  plan: PlanDto;
  handleRef: (element: Element | null) => void;
  canMoveUp: boolean;
  canMoveDown: boolean;
  onMoveUp: () => void;
  onMoveDown: () => void;
  showOwner: boolean;
}) {
  const meta = TIER_META[plan.tier];
  const Icon = Icons[meta?.icon ?? "crown"];
  const isTrialAddon = plan.tier === "FULL_ALL" && (plan.trialDurationDays ?? 0) > 0;

  return (
    <Link href={routes.authorPlanDetail(plan.id)} className="group block">
      <Card
        className={cn(
          "relative h-full p-5 transition hover:border-primary/60 hover:shadow-md",
          isTrialAddon && "ml-7 border-dashed bg-muted/20 hover:border-primary/40",
        )}
      >
        {isTrialAddon ? (
          <span className="absolute -left-4 top-6 hidden h-px w-4 bg-border sm:block" />
        ) : null}
        <div className="flex items-start gap-3">
          <button
            ref={handleRef}
            type="button"
            className="hidden cursor-grab touch-none self-center text-muted-foreground/40 hover:text-muted-foreground md:block"
            onClick={(e) => e.preventDefault()}
            aria-label="Перетащить"
            title="Перетащите чтобы изменить порядок"
          >
            <GripVertical className="h-4 w-4" />
          </button>
          {/* Touch reorder fallback — drag is unreliable on touch (md:hidden). */}
          <div className="flex flex-col gap-0.5 self-center md:hidden">
            <Button
              type="button"
              variant="ghost"
              size="icon"
              className="min-touch size-8 text-muted-foreground/60 hover:text-foreground"
              onClick={(e) => {
                e.preventDefault();
                e.stopPropagation();
                onMoveUp();
              }}
              disabled={!canMoveUp}
              aria-label="Переместить выше"
            >
              <Icons.chevronUp className="h-4 w-4" />
            </Button>
            <Button
              type="button"
              variant="ghost"
              size="icon"
              className="min-touch size-8 text-muted-foreground/60 hover:text-foreground"
              onClick={(e) => {
                e.preventDefault();
                e.stopPropagation();
                onMoveDown();
              }}
              disabled={!canMoveDown}
              aria-label="Переместить ниже"
            >
              <Icons.chevronDown className="h-4 w-4" />
            </Button>
          </div>
          <div className="flex size-10 shrink-0 items-center justify-center rounded-lg bg-primary/10 text-primary">
            <Icon className="size-5" />
          </div>
          <div className="min-w-0 flex-1 space-y-2">
            <div className="flex flex-wrap items-center gap-2">
              <h3 className="truncate text-base font-semibold leading-tight">{plan.displayName}</h3>
            </div>
            <div className="flex flex-wrap items-center gap-1.5">
              <Badge variant="outline" className="text-[11px] font-normal">
                {meta?.label ?? plan.tier}
              </Badge>
              {plan.isPublic ? (
                <Badge className="bg-emerald-500/15 text-emerald-600 hover:bg-emerald-500/20 text-[11px] font-normal dark:text-emerald-400">
                  Опубликован
                </Badge>
              ) : (
                <Badge variant="secondary" className="text-[11px] font-normal">
                  Черновик
                </Badge>
              )}
              {plan.archivedAt ? (
                <Badge variant="outline" className="text-[11px] font-normal text-muted-foreground">
                  В архиве
                </Badge>
              ) : null}
              {isTrialAddon ? (
                <Badge variant="outline" className="text-[11px] font-normal text-muted-foreground">
                  Дополнение к полному доступу
                </Badge>
              ) : null}
              {showOwner ? (
                <Badge variant="outline" className="text-[11px] font-normal text-muted-foreground">
                  Автор: {shortId(plan.authorId)}
                </Badge>
              ) : null}
            </div>
            {plan.shortDescription ? (
              <p className="line-clamp-2 text-sm text-muted-foreground">{plan.shortDescription}</p>
            ) : (
              <p className="text-sm italic text-muted-foreground/60">Нет описания</p>
            )}
            <div className="flex items-center gap-3 text-xs text-muted-foreground">
              <span className="font-mono">/{plan.slug}</span>
              {plan.priceCents ? (
                <span className="font-medium text-foreground">
                  {(plan.priceCents / 100).toLocaleString("ru")} {plan.currency}
                </span>
              ) : (
                <span>Бесплатно</span>
              )}
            </div>
          </div>
          <Icons.chevronRight className="size-4 shrink-0 text-muted-foreground/40 transition group-hover:translate-x-0.5 group-hover:text-foreground" />
        </div>
      </Card>
    </Link>
  );
}

function shortId(value: string) {
  return value.length > 8 ? value.slice(0, 8) : value;
}
