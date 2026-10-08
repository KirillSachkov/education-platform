"use client";

import { useState, type ReactNode } from "react";
import { NotificationDetailDialog, type Notification } from "@/entities/notification";
import { useIsAuthenticated } from "@/shared/auth";
import { cn } from "@/shared/lib/css";
import { Button } from "@/shared/ui/kit/button";
import { Popover, PopoverContent, PopoverTrigger } from "@/shared/ui/kit/popover";
import { Icons } from "@/shared/ui/icons";
import { useNotificationStream } from "../model/use-notification-stream";
import { useUnreadCount } from "../model/use-unread-count";
import { NotificationList } from "./notification-list";

/**
 * `topSlot` is rendered above the notification list inside the dropdown — used to
 * inject the Telegram-link prompt from the widget layer without features/notifications
 * importing features/telegram-link (FSD cross-slice rule).
 */
export function NotificationBell({ topSlot }: { topSlot?: ReactNode }) {
  const isAuthenticated = useIsAuthenticated();
  const { count } = useUnreadCount();
  const [open, setOpen] = useState(false);
  // Диалог живёт СНАРУЖИ Popover: внутри PopoverContent он размонтировался бы
  // вместе с закрытием поповера (#708).
  const [detail, setDetail] = useState<Notification | null>(null);

  // Opens SSE stream internally when authenticated; no-op otherwise.
  useNotificationStream();

  if (!isAuthenticated) {
    return null;
  }

  const badgeLabel = count > 99 ? "99+" : String(count);

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger asChild>
        <Button
          variant="ghost"
          size="icon"
          className="relative size-8 rounded-xl"
          aria-label={count > 0 ? `Уведомления: ${count} непрочитанных` : "Уведомления"}
        >
          <Icons.notification size={15} className="text-muted-foreground" />
          {/* Badge stays mounted; data-open drives the slide-in + dot pop so it
              can animate both in (0→N) and out (N→0) instead of hard-mounting. */}
          <span
            className="t-badge absolute -top-0.5 -right-0.5"
            data-open={count > 0 ? "true" : "false"}
            aria-hidden
          >
            <span
              className={cn(
                "t-badge-dot min-w-4 h-4 px-1",
                "rounded-full bg-primary text-primary-foreground",
                "text-[10px] font-semibold leading-none",
                "flex items-center justify-center tabular-nums",
              )}
            >
              {badgeLabel}
            </span>
          </span>
        </Button>
      </PopoverTrigger>
      <PopoverContent align="end" sideOffset={8} className="w-[400px] max-w-[calc(100vw-1rem)] p-0">
        {topSlot}
        <NotificationList
          onNavigate={() => setOpen(false)}
          onOpenDetail={(notification) => {
            setOpen(false);
            setDetail(notification);
          }}
        />
      </PopoverContent>
      <NotificationDetailDialog
        notification={detail}
        onOpenChange={(dialogOpen) => {
          if (!dialogOpen) setDetail(null);
        }}
      />
    </Popover>
  );
}
