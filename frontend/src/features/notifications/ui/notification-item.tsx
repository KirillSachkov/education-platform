"use client";

import { useRouter } from "next/navigation";
import { notificationHref, type Notification } from "@/entities/notification";
import { cn } from "@/shared/lib/css";
import { formatRelativeDate } from "@/shared/lib/date";
import { useMarkAsRead } from "../model/use-mark-as-read";

interface NotificationItemProps {
  notification: Notification;
  onNavigate?: () => void;
  /** Уведомление без targetUrl: открыть полный текст в модалке (#708). */
  onOpenDetail?: (notification: Notification) => void;
}

export function NotificationItem({
  notification,
  onNavigate,
  onOpenDetail,
}: NotificationItemProps) {
  const router = useRouter();
  const { markAsRead } = useMarkAsRead();
  const isUnread = !notification.readAt;
  const href = notificationHref(notification);

  const handleClick = () => {
    if (isUnread) {
      markAsRead(notification.id);
    }
    if (href) {
      onNavigate?.();
      router.push(href);
    } else {
      onOpenDetail?.(notification);
    }
  };

  const interactive = Boolean(href) || Boolean(onOpenDetail) || isUnread;

  return (
    <button
      type="button"
      onClick={handleClick}
      disabled={!interactive}
      className={cn(
        "w-full text-left px-4 py-3 transition-colors border-l-2",
        "hover:bg-accent/30",
        "disabled:cursor-default disabled:hover:bg-transparent",
        isUnread ? "border-l-primary bg-primary/5" : "border-l-transparent",
      )}
    >
      <div className="flex items-start gap-2">
        <div className="flex-1 min-w-0">
          <div className="flex items-start gap-2">
            <p
              className={cn(
                "text-sm leading-snug line-clamp-2 flex-1 wrap-anywhere",
                isUnread ? "font-semibold" : "font-medium text-muted-foreground",
              )}
            >
              {notification.title}
            </p>
            {isUnread && <span className="mt-1.5 size-1.5 rounded-full bg-primary shrink-0" />}
          </div>
          {notification.body && (
            <p className="mt-1 text-xs text-muted-foreground line-clamp-2">{notification.body}</p>
          )}
          <p className="mt-1.5 text-[11px] text-muted-foreground/80">
            {formatRelativeDate(notification.createdAt)}
          </p>
        </div>
      </div>
    </button>
  );
}
