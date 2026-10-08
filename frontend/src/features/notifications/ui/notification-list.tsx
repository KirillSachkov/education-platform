"use client";

import type { Notification } from "@/entities/notification";
import { getErrorMessage } from "@/shared/api";
import { routes } from "@/shared/config/routes";
import { Button } from "@/shared/ui/kit/button";
import { Separator } from "@/shared/ui/kit/separator";
import { Icons } from "@/shared/ui/icons";
import Link from "next/link";
import { useMarkAllAsRead } from "../model/use-mark-all-as-read";
import { useNotifications } from "../model/use-notifications";
import { useUnreadCount } from "../model/use-unread-count";
import { NotificationItem } from "./notification-item";

interface NotificationListProps {
  onNavigate?: () => void;
  /** Уведомление без targetUrl: открыть полный текст в модалке (#708). */
  onOpenDetail?: (notification: Notification) => void;
}

export function NotificationList({ onNavigate, onOpenDetail }: NotificationListProps) {
  const { items, hasNextPage, fetchNextPage, isLoading, isFetchingNextPage, error, refetch } =
    useNotifications({ limit: 20 });
  const { count: unreadCount } = useUnreadCount();
  const { markAllAsRead, isPending: isMarkingAll } = useMarkAllAsRead();

  return (
    <div className="flex flex-col">
      <header className="flex items-center justify-between px-4 py-3">
        <div>
          <p className="text-sm font-semibold">Уведомления</p>
          {unreadCount > 0 && (
            <p className="text-xs text-muted-foreground">Непрочитано: {unreadCount}</p>
          )}
        </div>
        {unreadCount > 0 && (
          <Button
            variant="ghost"
            size="sm"
            className="gap-1.5 h-7 px-2 text-xs"
            onClick={() => markAllAsRead()}
            disabled={isMarkingAll}
          >
            <Icons.checkAll size={13} />
            Прочитать все
          </Button>
        )}
      </header>
      <Separator />

      {isLoading ? (
        <div className="flex items-center justify-center py-10">
          <Icons.loading size={20} className="animate-spin text-muted-foreground" />
        </div>
      ) : error ? (
        <div className="flex flex-col items-center justify-center gap-2 py-10 px-4 text-center">
          <Icons.error size={20} className="text-destructive" />
          <p className="text-sm text-muted-foreground">
            {getErrorMessage(error, "Не удалось загрузить уведомления")}
          </p>
          <Button variant="outline" size="sm" onClick={() => refetch()}>
            Повторить
          </Button>
        </div>
      ) : items.length === 0 ? (
        <div className="flex flex-col items-center justify-center gap-2 py-10 px-4 text-center">
          <Icons.notification size={22} className="text-muted-foreground" />
          <p className="text-sm text-muted-foreground">Уведомлений пока нет</p>
        </div>
      ) : (
        <div className="max-h-[400px] overflow-y-auto overscroll-contain">
          <ul className="divide-y divide-border/60">
            {items.map((notification) => (
              <li key={notification.id}>
                <NotificationItem
                  notification={notification}
                  onNavigate={onNavigate}
                  onOpenDetail={onOpenDetail}
                />
              </li>
            ))}
          </ul>

          {hasNextPage && (
            <div className="flex justify-center py-3">
              <Button
                variant="ghost"
                size="sm"
                onClick={() => fetchNextPage()}
                disabled={isFetchingNextPage}
              >
                {isFetchingNextPage ? (
                  <>
                    <Icons.loading size={13} className="animate-spin" />
                    Загружаем...
                  </>
                ) : (
                  "Показать ещё"
                )}
              </Button>
            </div>
          )}
        </div>
      )}

      <Separator />
      <div className="px-3 py-2 text-center">
        <Button
          variant="link"
          size="sm"
          className="h-auto p-0 text-xs"
          asChild
          onClick={onNavigate}
        >
          <Link href={routes.notifications}>Открыть центр уведомлений</Link>
        </Button>
      </div>
    </div>
  );
}
