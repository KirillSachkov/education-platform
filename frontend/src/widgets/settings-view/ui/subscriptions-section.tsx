"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import {
  notificationQueryOptions,
  notificationsApi,
  type Subscription,
} from "@/entities/notification";
import { getErrorMessage } from "@/shared/api";
import { Button } from "@/shared/ui/kit/button";
import { Icons } from "@/shared/ui/icons";

/**
 * Раздел «Подписки» — список курсов / авторов / модулей, на обновления которых
 * пользователь подписан. Подписки создаются автоматически при записи на курс.
 */
export function SubscriptionsSection() {
  const { data, isLoading, error } = useQuery(notificationQueryOptions.subscriptions());
  const queryClient = useQueryClient();

  const unsubscribeMutation = useMutation({
    mutationFn: (id: string) => notificationsApi.unsubscribe(id),
    onSuccess: async () => {
      toast.success("Подписка удалена");
      await queryClient.invalidateQueries({
        queryKey: notificationQueryOptions.subscriptionsKey(),
      });
    },
    onError: (err) => toast.error(getErrorMessage(err, "Не удалось отписаться")),
  });

  if (isLoading) {
    return (
      <SectionCard>
        <div className="flex items-center justify-center py-6">
          <Icons.loading size={18} className="animate-spin text-muted-foreground" />
        </div>
      </SectionCard>
    );
  }

  if (error) {
    return (
      <SectionCard>
        <div className="flex items-center gap-2 text-sm text-destructive">
          <Icons.error size={16} />
          Не удалось загрузить подписки
        </div>
      </SectionCard>
    );
  }

  const subscriptions = (data ?? []) as Subscription[];

  if (subscriptions.length === 0) {
    return (
      <SectionCard>
        <div className="flex flex-col items-center justify-center text-center py-8 gap-3">
          <span className="flex size-12 items-center justify-center rounded-2xl bg-muted/60 text-muted-foreground">
            <Icons.notification size={20} />
          </span>
          <div className="space-y-1">
            <p className="text-sm font-medium">Подписок пока нет</p>
            <p className="text-xs text-muted-foreground max-w-sm">
              Они создаются автоматически при записи на курс — тогда вы будете получать уведомления
              о новых материалах.
            </p>
          </div>
        </div>
      </SectionCard>
    );
  }

  return (
    <SectionCard>
      <p className="text-xs text-muted-foreground mb-3 px-1">
        Вы получаете уведомления о новых материалах по следующим подпискам.
      </p>
      <ul className="space-y-1">
        {subscriptions.map((sub) => (
          <SubscriptionRow
            key={sub.id}
            subscription={sub}
            onUnsubscribe={() => unsubscribeMutation.mutate(sub.id)}
            isPending={unsubscribeMutation.isPending}
          />
        ))}
      </ul>
    </SectionCard>
  );
}

function SectionCard({ children }: { children: React.ReactNode }) {
  return (
    <section className="rounded-2xl border border-border/50 bg-card/40 p-3 sm:p-4">
      {children}
    </section>
  );
}

function SubscriptionRow({
  subscription,
  onUnsubscribe,
  isPending,
}: {
  subscription: Subscription;
  onUnsubscribe: () => void;
  isPending: boolean;
}) {
  const meta = entityMeta(subscription.entityType);
  const title = subscription.title ?? `${meta.fallbackLabel} ${shortenId(subscription.entityId)}`;
  return (
    <li className="flex items-center gap-3.5 rounded-xl px-3 py-3 hover:bg-muted/40 transition-colors">
      <span
        className={`flex size-9 shrink-0 items-center justify-center rounded-xl ${meta.iconClass}`}
      >
        <meta.Icon className="size-4" />
      </span>
      <div className="min-w-0 flex-1">
        <p className="text-sm font-medium text-foreground truncate">{title}</p>
        <p className="mt-0.5 text-xs text-muted-foreground truncate">{meta.typeLabel}</p>
      </div>
      <Button
        type="button"
        variant="ghost"
        size="sm"
        className="text-muted-foreground hover:text-destructive shrink-0"
        disabled={isPending}
        onClick={onUnsubscribe}
      >
        Отписаться
      </Button>
    </li>
  );
}

function entityMeta(entityType: Subscription["entityType"]) {
  if (entityType === "course") {
    return {
      Icon: Icons.course,
      typeLabel: "Курс",
      fallbackLabel: "Курс",
      iconClass: "bg-primary/10 text-primary",
    };
  }
  if (entityType === "author") {
    return {
      Icon: Icons.users,
      typeLabel: "Автор",
      fallbackLabel: "Автор",
      iconClass: "bg-blue-dim text-blue",
    };
  }
  if (entityType === "module") {
    return {
      Icon: Icons.module,
      typeLabel: "Модуль",
      fallbackLabel: "Модуль",
      iconClass: "bg-purple-dim text-purple",
    };
  }
  return {
    Icon: Icons.notification,
    typeLabel: entityType,
    fallbackLabel: entityType,
    iconClass: "bg-muted/60 text-muted-foreground",
  };
}

function shortenId(id: string) {
  return id.length > 12 ? `${id.slice(0, 8)}…` : id;
}
