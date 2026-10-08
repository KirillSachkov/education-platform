"use client";

import { useState } from "react";
import { formatRelativeDate } from "@/shared/lib/date";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@/shared/ui/kit/dialog";
import { NotificationTypeLabels, type Notification } from "../model/types";

interface NotificationDetailDialogProps {
  /** `null` — диалог закрыт. */
  notification: Notification | null;
  onOpenChange: (open: boolean) => void;
}

/**
 * Полный текст уведомления без ссылки (#708): списки обрезают title/body
 * line-clamp'ом, а перейти таким уведомлениям некуда — открываем модалку.
 */
export function NotificationDetailDialog({
  notification,
  onOpenChange,
}: NotificationDetailDialogProps) {
  // Держим последнее непустое значение: consumer'ы обнуляют notification в момент
  // закрытия, а DialogContent ещё ~200ms доигрывает exit-анимацию — без этого
  // контент исчезал бы из уезжающего диалога (флэш пустой коробки).
  const [lastNotification, setLastNotification] = useState(notification);
  if (notification && notification !== lastNotification) {
    setLastNotification(notification);
  }

  const shown = notification ?? lastNotification;

  return (
    <Dialog open={Boolean(notification)} onOpenChange={onOpenChange}>
      <DialogContent>
        {shown && (
          <>
            <DialogHeader className="text-left">
              <DialogDescription>
                {NotificationTypeLabels[shown.type] ?? "Уведомление"}
                {" · "}
                <time dateTime={shown.createdAt}>{formatRelativeDate(shown.createdAt)}</time>
              </DialogDescription>
              <DialogTitle className="text-base leading-snug wrap-anywhere">
                {shown.title}
              </DialogTitle>
            </DialogHeader>
            {shown.body && (
              <div className="max-h-[60vh] overflow-y-auto overscroll-contain">
                <p className="text-sm text-muted-foreground whitespace-pre-wrap wrap-anywhere">
                  {shown.body}
                </p>
              </div>
            )}
          </>
        )}
      </DialogContent>
    </Dialog>
  );
}
