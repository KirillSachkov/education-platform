"use client";

import { useQuery } from "@tanstack/react-query";
import { adminCrossServiceQueryOptions } from "@/entities/admin-cross-service";
import { Button } from "@/shared/ui/kit/button";
import { EmptyState } from "@/shared/ui/kit/empty-state";
import { Icons } from "@/shared/ui/icons";
import { Skeleton } from "@/shared/ui/kit/skeleton";
import { useResendTelegramInvites } from "../model/use-resend-telegram-invites";
import { DiagnosticsList } from "./diagnostics-list";
import { GithubStatusBlock } from "./github-status-block";
import { GrantBlock } from "./grant-block";
import { PaidOrders } from "./paid-orders";
import { TelegramIdentityBlock } from "./telegram-identity-block";

type AccessCommunitiesTabProps = {
  userId: string;
};

/**
 * Админ-вкладка «Доступы и сообщества» (#444): сводка post-purchase юзера —
 * Telegram-привязка, GitHub/App-статус, оплаченные заказы, активные гранты
 * (с onboarding-прогрессом, Telegram-членством и чатами плана), diagnostics.
 * Плюс безопасные support-действия
 * (перепроверить членство / переотправить приветствие — per-plan; обновить
 * приглашения — на уровне юзера). Mobile-first, dual-render table/card.
 */
export function AccessCommunitiesTab({ userId }: AccessCommunitiesTabProps) {
  const query = useQuery(adminCrossServiceQueryOptions.getPostPurchaseStatusOptions(userId));
  const resyncInvites = useResendTelegramInvites(userId);

  if (query.isLoading) {
    return (
      <div className="space-y-3">
        <Skeleton className="h-16 w-full" />
        <Skeleton className="h-24 w-full" />
        <Skeleton className="h-40 w-full" />
      </div>
    );
  }

  if (query.isError || !query.data) {
    return (
      <EmptyState
        variant="card"
        title="Не удалось загрузить доступы и сообщества"
        icon={Icons.warning}
      />
    );
  }

  const status = query.data;

  return (
    <div className="space-y-6">
      {/* Telegram + GitHub identity (top) */}
      <TelegramIdentityBlock userId={userId} />
      <GithubStatusBlock userId={userId} />

      {/* User-level support action */}
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p className="text-sm text-muted-foreground">
          {status.hasPaidOrder ? "Есть оплаченные заказы" : "Оплаченных заказов нет"}
        </p>
        <Button
          type="button"
          variant="outline"
          size="sm"
          className="min-touch gap-1.5"
          disabled={resyncInvites.isPending}
          onClick={() => resyncInvites.mutate()}
        >
          {resyncInvites.isPending ? (
            <Icons.loading className="size-3.5 animate-spin" />
          ) : (
            <Icons.send className="size-3.5" />
          )}
          Обновить приглашения
        </Button>
      </div>

      {/* Diagnostics */}
      <DiagnosticsList diagnostics={status.diagnostics} />

      {/* Paid orders */}
      <section className="space-y-3">
        <h2 className="text-sm font-semibold">Заказы</h2>
        <PaidOrders orders={status.orders} />
      </section>

      {/* Active grants */}
      <section className="space-y-3">
        <h2 className="text-sm font-semibold">Активные доступы</h2>
        {status.activeGrants.length === 0 ? (
          <EmptyState variant="card" title="Нет активных доступов" icon={Icons.locked} />
        ) : (
          <div className="space-y-3">
            {status.activeGrants.map((grant) => (
              <GrantBlock key={grant.grantId} userId={userId} grant={grant} />
            ))}
          </div>
        )}
      </section>
    </div>
  );
}
