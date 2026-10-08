"use client";

import {
  myPlansQueryOptions,
  planGrantsInfiniteQueryOptions,
  planInvitesQueryOptions,
  type PlanDto,
  type PlanGrantDto,
  type PlanGrantSource,
  type PlanGrantStatus,
  type PlanTier,
} from "@/entities/access-plan";
import { useDebouncedValue } from "@/shared/hooks";
import { routes } from "@/shared/config/routes";
import { Avatar, AvatarFallback, AvatarImage } from "@/shared/ui/kit/avatar";
import { Badge } from "@/shared/ui/kit/badge";
import { Button } from "@/shared/ui/kit/button";
import { Card } from "@/shared/ui/kit/card";
import { Input } from "@/shared/ui/kit/input";
import { Label } from "@/shared/ui/kit/label";
import { Icons } from "@/shared/ui/icons";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/shared/ui/kit/tabs";
import { EmptyState } from "@/shared/ui/kit/empty-state";
import { useInfiniteQuery, useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { type ReactNode, useState } from "react";
import { toast } from "sonner";
import {
  useArchivePlan,
  useCreateInvite,
  useDeleteInvite,
  useDeletePlan,
  usePublishPlan,
  useRevokeGrant,
  useUnarchivePlan,
  useUnpublishPlan,
} from "../model/use-author-plans";
import { GrantUserDialog } from "./grant-user-dialog";
import { EditPlanForm } from "./edit-plan-form";
import { PlanStatsTab } from "./plan-stats-tab";
import { DeleteConfirmDialog } from "@/shared/ui/components/delete-confirm-dialog";

interface Props {
  planId: string;
  /**
   * Slot для редактора onboarding-flow плана. Композируется на странице,
   * чтобы `features/author-plans` не зависел от `features/plan-onboarding-edit`.
   */
  onboardingEditor?: ReactNode;
  /**
   * Slot для списка Telegram chat-bindings (рендерится внутри settings-таба
   * через `EditPlanForm`). См. описание выше — те же FSD-соображения.
   */
  chatBindings?: ReactNode;
  /**
   * Slot для редактора закреплённых материалов плана (`features/plan-home-pins-edit`).
   * Композируется на странице — те же FSD-соображения, что и `onboardingEditor`.
   */
  homePinsEditor?: ReactNode;
}

const TIER_META: Record<PlanTier, { label: string; icon: keyof typeof Icons }> = {
  FULL_ALL: { label: "Полный доступ", icon: "crown" },
  LEARN_ALL: { label: "Все материалы", icon: "library" },
  COURSE: { label: "Подборка курсов", icon: "grid" },
  SUBSCRIPTION: { label: "Подписка", icon: "rocket" },
  FREE: { label: "Бесплатный", icon: "gift" },
};

const GRANT_SOURCE_LABEL: Record<PlanGrantSource, string> = {
  INVITE_LINK: "По ссылке",
  ADMIN_GRANT: "Выдано вручную",
  MIGRATION: "Миграция",
  PURCHASE: "Покупка",
  TRIAL: "Бесплатный план",
  GITHUB_ORG: "GitHub-организация",
  TELEGRAM_F1: "Telegram",
  AUTO_FREE: "Автоматически",
};

const GRANT_STATUS_LABEL: Record<PlanGrantStatus, string> = {
  ACTIVE: "Активен",
  REVOKED: "Отозван",
  EXPIRED: "Истёк",
};

export function AuthorPlanDetail({
  planId,
  onboardingEditor,
  chatBindings,
  homePinsEditor,
}: Props) {
  const plansQuery = useQuery(myPlansQueryOptions());
  const plan = plansQuery.data?.find((p) => p.id === planId);

  if (plansQuery.isLoading) {
    return (
      <div className="mx-auto mt-12 max-w-4xl px-4 space-y-4">
        <div className="h-32 rounded-xl bg-muted/40 animate-pulse" />
        <div className="h-64 rounded-xl bg-muted/40 animate-pulse" />
      </div>
    );
  }
  if (!plan) {
    return (
      <div className="mx-auto mt-12 max-w-3xl px-4 text-center">
        <h2 className="text-xl font-semibold">План не найден</h2>
        <Button asChild variant="outline" className="mt-4">
          <Link href={routes.authorPlans}>К списку</Link>
        </Button>
      </div>
    );
  }

  const isTrialPlan = (plan.trialDurationDays ?? 0) > 0;

  return (
    <div className="mx-auto mt-8 max-w-4xl space-y-6 px-4 pb-16">
      <Link
        href={routes.authorPlans}
        className="inline-flex items-center gap-2 text-sm text-muted-foreground transition hover:text-foreground"
      >
        <Icons.back className="size-4" />
        К списку планов
      </Link>

      <PlanHeader plan={plan} />

      <Tabs defaultValue="stats">
        <TabsList>
          <TabsTrigger value="stats">Статистика</TabsTrigger>
          {!isTrialPlan ? <TabsTrigger value="invites">Пригласительные ссылки</TabsTrigger> : null}
          <TabsTrigger value="grants">Кому выдан доступ</TabsTrigger>
          {!isTrialPlan ? <TabsTrigger value="onboarding">Онбординг</TabsTrigger> : null}
          {!isTrialPlan ? <TabsTrigger value="pins">Закрепы</TabsTrigger> : null}
          {!isTrialPlan ? <TabsTrigger value="settings">Настройки</TabsTrigger> : null}
        </TabsList>
        <TabsContent value="stats" className="mt-4">
          <PlanStatsTab planId={planId} />
        </TabsContent>
        {!isTrialPlan ? (
          <TabsContent value="invites" className="mt-4">
            <InvitesTab planId={planId} />
          </TabsContent>
        ) : null}
        <TabsContent value="grants" className="mt-4">
          <GrantsTab planId={planId} />
        </TabsContent>
        {!isTrialPlan ? (
          <TabsContent value="onboarding" className="mt-4">
            {onboardingEditor}
          </TabsContent>
        ) : null}
        {!isTrialPlan ? (
          <TabsContent value="pins" className="mt-4">
            {homePinsEditor}
          </TabsContent>
        ) : null}
        {!isTrialPlan ? (
          <TabsContent value="settings" className="mt-4">
            <EditPlanForm planId={planId} chatBindings={chatBindings} />
          </TabsContent>
        ) : null}
      </Tabs>
    </div>
  );
}

function PlanHeader({ plan }: { plan: PlanDto }) {
  const publish = usePublishPlan();
  const unpublish = useUnpublishPlan();
  const archive = useArchivePlan();
  const unarchive = useUnarchivePlan();
  const del = useDeletePlan();

  const meta = TIER_META[plan.tier];
  const Icon = Icons[meta?.icon ?? "crown"];

  return (
    <Card className="overflow-hidden p-0">
      <div className="flex flex-col gap-6 p-6 sm:flex-row sm:items-start sm:justify-between">
        <div className="flex items-start gap-4">
          <div className="flex size-12 shrink-0 items-center justify-center rounded-xl bg-primary/10 text-primary">
            <Icon className="size-6" />
          </div>
          <div className="space-y-2">
            <h1 className="text-2xl font-semibold leading-tight tracking-tight">
              {plan.displayName}
            </h1>
            <div className="flex flex-wrap items-center gap-1.5 text-xs">
              <Badge variant="outline" className="font-normal">
                {meta?.label ?? plan.tier}
              </Badge>
              {plan.isPublic ? (
                <Badge className="bg-emerald-500/15 font-normal text-emerald-600 hover:bg-emerald-500/20 dark:text-emerald-400">
                  Опубликован
                </Badge>
              ) : (
                <Badge variant="secondary" className="font-normal">
                  Черновик
                </Badge>
              )}
              {plan.archivedAt ? (
                <Badge variant="outline" className="font-normal text-muted-foreground">
                  В архиве
                </Badge>
              ) : null}
              <span className="ml-1 font-mono text-muted-foreground">/{plan.slug}</span>
            </div>
            {plan.shortDescription ? (
              <p className="max-w-prose pt-1 text-sm text-muted-foreground">
                {plan.shortDescription}
              </p>
            ) : null}
          </div>
        </div>

        <div className="flex flex-wrap gap-2 sm:flex-col sm:items-end">
          {plan.archivedAt ? (
            <Button
              size="sm"
              variant="outline"
              disabled={unarchive.isPending}
              onClick={() => unarchive.mutate(plan.id)}
            >
              <Icons.archive className="size-4" />
              Восстановить
            </Button>
          ) : (
            <>
              {plan.isPublic ? (
                <Button
                  size="sm"
                  variant="outline"
                  disabled={unpublish.isPending}
                  onClick={() => unpublish.mutate(plan.id)}
                >
                  Снять с публикации
                </Button>
              ) : (
                <Button
                  size="sm"
                  disabled={publish.isPending}
                  onClick={() => publish.mutate(plan.id)}
                >
                  Опубликовать
                </Button>
              )}
              <Button
                size="sm"
                variant="ghost"
                disabled={archive.isPending}
                onClick={() => archive.mutate(plan.id)}
              >
                <Icons.archive className="size-4" />В архив
              </Button>
            </>
          )}

          <DeleteConfirmDialog
            title="Удалить план навсегда?"
            description={
              <>
                Безвозвратно удалит план «{plan.displayName}» и все его настройки —
                пригласительные ссылки, онбординг, закрепы материалов, Telegram-привязки.
                Доступно только для плана <strong>без оплат и активных доступов</strong> —
                иначе используйте «В архив».
              </>
            }
            confirmLabel="Удалить"
            isPending={del.isPending}
            onConfirm={() => del.mutate(plan.id)}
            trigger={
              <Button
                size="sm"
                variant="ghost"
                className="text-destructive hover:text-destructive"
              >
                <Icons.delete className="size-4" />
                Удалить
              </Button>
            }
          />
        </div>
      </div>
    </Card>
  );
}

function InvitesTab({ planId }: { planId: string }) {
  const invitesQuery = useQuery(planInvitesQueryOptions(planId));
  const create = useCreateInvite(planId);
  const deleteInvite = useDeleteInvite(planId);
  const [label, setLabel] = useState("");
  const [maxUses, setMaxUses] = useState("");

  const onSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    create.mutate({
      multiUse: true,
      maxUses: maxUses ? Number.parseInt(maxUses, 10) || null : null,
      label: label.trim() || null,
    });
    setLabel("");
    setMaxUses("");
  };

  return (
    <div className="space-y-4">
      <Card className="p-5">
        <form onSubmit={onSubmit} className="grid gap-3 sm:grid-cols-[1fr_160px_auto] sm:items-end">
          <div className="space-y-1.5">
            <Label htmlFor="invite-label" className="text-xs">
              Метка (для себя)
            </Label>
            <Input
              id="invite-label"
              value={label}
              onChange={(e) => setLabel(e.target.value)}
              placeholder="например, promo-jan"
            />
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="invite-max-uses" className="text-xs">
              Лимит активаций
            </Label>
            <Input
              id="invite-max-uses"
              type="number"
              min={1}
              value={maxUses}
              onChange={(e) => setMaxUses(e.target.value)}
              placeholder="без лимита"
            />
          </div>
          <Button type="submit" disabled={create.isPending}>
            <Icons.add className="size-4" />
            Выпустить
          </Button>
        </form>
      </Card>

      {invitesQuery.data && invitesQuery.data.length > 0 ? (
        <div className="space-y-2">
          {invitesQuery.data.map((invite) => (
            <Card key={invite.id} className="p-4">
              <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
                <div className="min-w-0 flex-1 space-y-2">
                  <code className="block truncate rounded-md bg-muted px-2.5 py-1.5 font-mono text-xs">
                    {inviteUrl(invite.token)}
                  </code>
                  <div className="flex flex-wrap items-center gap-3 text-xs text-muted-foreground">
                    <span>
                      Активаций: <span className="text-foreground">{invite.usageCount}</span>
                      {invite.maxUses ? ` / ${invite.maxUses}` : " (без лимита)"}
                    </span>
                    {invite.label ? (
                      <span>
                        Метка: <span className="text-foreground">{invite.label}</span>
                      </span>
                    ) : null}
                  </div>
                </div>
                <div className="flex items-center gap-1 self-start sm:self-auto">
                  {!invite.isActive ? (
                    <Badge variant="outline" className="mr-1">
                      Отозвана
                    </Badge>
                  ) : null}
                  <Button
                    size="sm"
                    variant="ghost"
                    type="button"
                    onClick={() => copyInviteLink(invite.token)}
                    title="Скопировать ссылку"
                  >
                    <Icons.copy className="size-4" />
                  </Button>
                  <DeleteConfirmDialog
                    title="Удалить ссылку?"
                    description={
                      <>
                        Ссылка пропадёт из списка. Уже активированные доступы
                        останутся — это аудит-запись отдельная от ссылки.
                        {invite.label ? (
                          <>
                            <br />
                            <br />
                            Метка: <strong>{invite.label}</strong>
                          </>
                        ) : null}
                      </>
                    }
                    confirmLabel="Удалить"
                    isPending={deleteInvite.isPending}
                    onConfirm={() => deleteInvite.mutate(invite.id)}
                    trigger={
                      <Button
                        size="sm"
                        variant="ghost"
                        type="button"
                        title="Удалить ссылку"
                      >
                        <Icons.delete className="size-4" />
                      </Button>
                    }
                  />
                </div>
              </div>
            </Card>
          ))}
        </div>
      ) : (
        <EmptyState
          variant="dashed"
          icon={Icons.copy}
          title="Ссылок ещё нет"
          description="Выпустите первую пригласительную ссылку — её можно отдать ученику или опубликовать в канале. По клику и логину пользователь получит доступ к этому плану."
        />
      )}
    </div>
  );
}

async function copyInviteLink(token: string): Promise<void> {
  try {
    await navigator.clipboard.writeText(inviteUrl(token));
    toast.success("Ссылка скопирована");
  } catch {
    toast.error("Не удалось скопировать ссылку");
  }
}

function GrantsTab({ planId }: { planId: string }) {
  const [search, setSearch] = useState("");
  const debouncedSearch = useDebouncedValue(search.trim(), 300);
  const grantsQuery = useInfiniteQuery(
    planGrantsInfiniteQueryOptions(planId, debouncedSearch || undefined),
  );
  const revoke = useRevokeGrant(planId);
  const [grantDialogOpen, setGrantDialogOpen] = useState(false);

  const grants = grantsQuery.data?.items ?? [];
  const isInitialLoading = grantsQuery.isLoading;
  const isSearchActive = debouncedSearch.length > 0;

  return (
    <div className="space-y-3">
      <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <div className="relative w-full sm:max-w-xs">
          <Icons.search className="pointer-events-none absolute left-2.5 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" />
          <Input
            type="search"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Поиск по имени, username, Telegram"
            className="pl-9"
          />
        </div>
        <Button size="sm" onClick={() => setGrantDialogOpen(true)}>
          <Icons.add className="size-4" />
          Выдать вручную
        </Button>
      </div>

      <p className="text-xs text-muted-foreground px-1">
        {isInitialLoading
          ? "Загружаем…"
          : grants.length === 0 && isSearchActive
            ? `Ничего не найдено по запросу «${debouncedSearch}»`
            : grants.length === 0
              ? "Сюда попадают пользователи, активировавшие приглашение или получившие grant вручную."
              : isSearchActive
                ? `Совпадений по поиску: ${grants.length}`
                : `На странице ${grants.length} ${grantsLabel(grants.length)}${
                    grantsQuery.hasNextPage ? " (есть ещё)" : ""
                  }`}
      </p>

      {isInitialLoading ? (
        <div className="space-y-2">
          {[1, 2, 3].map((i) => (
            <div key={i} className="h-20 rounded-lg bg-muted/40 animate-pulse" />
          ))}
        </div>
      ) : grants.length === 0 ? (
        <EmptyState
          variant="dashed"
          icon={isSearchActive ? Icons.search : Icons.users}
          title={isSearchActive ? "Не найдено" : "Никто пока не получил доступ"}
          description={
            isSearchActive
              ? "Попробуйте другой запрос — поиск идёт по имени, username и Telegram."
              : "Активируйте приглашение или выдайте grant вручную через кнопку выше."
          }
        />
      ) : (
        <div className="space-y-2">
          {grants.map((grant) => (
            <GrantRow
              key={grant.id}
              grant={grant}
              isRevoking={revoke.isPending}
              onRevoke={(grantId: string) => revoke.mutate({ grantId })}
            />
          ))}

          {grantsQuery.hasNextPage && !isSearchActive ? (
            <div className="pt-2 text-center">
              <Button
                variant="outline"
                size="sm"
                disabled={grantsQuery.isFetchingNextPage}
                onClick={() => grantsQuery.fetchNextPage()}
              >
                {grantsQuery.isFetchingNextPage ? "Загружаем…" : "Показать ещё"}
              </Button>
            </div>
          ) : null}
        </div>
      )}

      <GrantUserDialog
        planId={planId}
        open={grantDialogOpen}
        onOpenChange={setGrantDialogOpen}
      />
    </div>
  );
}

function GrantRow({
  grant,
  isRevoking,
  onRevoke,
}: {
  grant: PlanGrantDto;
  isRevoking: boolean;
  onRevoke: (grantId: string) => void;
}) {
  const title = grant.userDisplayName ?? grant.userUsername ?? grant.userEmail ?? grant.userId;
  const subtitle = grant.userEmail && grant.userEmail !== title ? grant.userEmail : null;
  const initials = (title ?? "??").slice(0, 2).toUpperCase();
  const avatarSrc = grant.userAvatarId ? `/api/files/${grant.userAvatarId}/content` : undefined;

  return (
    <Card className="p-4">
      <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <div className="flex min-w-0 items-center gap-3">
          <Avatar className="size-9 shrink-0">
            <AvatarImage src={avatarSrc} alt={title} />
            <AvatarFallback>{initials}</AvatarFallback>
          </Avatar>
          <div className="min-w-0 space-y-1.5">
            <p className="truncate text-sm font-medium">{title}</p>
            {subtitle ? (
              <p className="truncate text-xs text-muted-foreground">{subtitle}</p>
            ) : null}
            <div className="flex flex-wrap items-center gap-1.5 text-xs">
              <Badge variant="outline" className="font-normal">
                {GRANT_SOURCE_LABEL[grant.source] ?? grant.source}
              </Badge>
              <Badge
                variant={grant.status === "ACTIVE" ? "secondary" : "outline"}
                className="font-normal"
              >
                {GRANT_STATUS_LABEL[grant.status] ?? grant.status}
              </Badge>
              <span className="text-muted-foreground">выдан {formatDate(grant.grantedAt)}</span>
              {grant.expiresAt ? (
                <span className="text-muted-foreground">до {formatDate(grant.expiresAt)}</span>
              ) : null}
            </div>
          </div>
        </div>
        {grant.status === "ACTIVE" ? (
          <DeleteConfirmDialog
            title="Отозвать доступ?"
            description={
              <>
                Пользователь <strong>{title}</strong> потеряет доступ к содержимому
                плана. Если у плана включён auto-kick — будет исключён из привязанных
                Telegram-чатов. Действие необратимо.
              </>
            }
            confirmLabel="Отозвать"
            isPending={isRevoking}
            onConfirm={() => onRevoke(grant.id)}
            trigger={
              <Button size="sm" variant="outline" className="self-start sm:self-auto">
                Отозвать
              </Button>
            }
          />
        ) : null}
      </div>
    </Card>
  );
}

function grantsLabel(count: number): string {
  const lastTwo = count % 100;
  if (lastTwo >= 11 && lastTwo <= 14) return "пользователей";
  const last = count % 10;
  if (last === 1) return "пользователь";
  if (last >= 2 && last <= 4) return "пользователя";
  return "пользователей";
}

function inviteUrl(token: string): string {
  if (typeof window !== "undefined") {
    return `${window.location.origin}/invite/${token}`;
  }
  return `/invite/${token}`;
}

function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString("ru-RU");
}
