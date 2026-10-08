"use client";

import { useState } from "react";
import {
  NotificationTypeLabels,
  NotificationTypes,
  type NotificationPreference,
  type NotificationType,
} from "@/entities/notification";
import { Button } from "@/shared/ui/kit/button";
import { Separator } from "@/shared/ui/kit/separator";
import { Switch } from "@/shared/ui/kit/switch";
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from "@/shared/ui/kit/tooltip";
import { Icons } from "@/shared/ui/icons";
import {
  useNotificationPreferences,
  useUpdateNotificationPreferences,
} from "../model/use-notification-preferences";
import { useMyProfile } from "../model/use-my-profile";

// Типы, которыми пользователь реально может управлять.
// Welcome / TelegramLinked — системные transactional, не настраиваются
// (показать welcome-письмо после регистрации — важнее opt-in эстетики).
const USER_CONFIGURABLE_TYPES: NotificationType[] = [
  NotificationTypes.CourseEnrolled,
  NotificationTypes.MaterialPublished,
  NotificationTypes.IssueSubmissionApproved,
  NotificationTypes.IssueSubmissionChangesRequested,
  NotificationTypes.IssueSubmissionAwaitingReview,
  NotificationTypes.AuthorAnnouncement,
  NotificationTypes.CommentReplied,
  NotificationTypes.CommentOnOwnContent,
];

function equal(a: NotificationPreference, b: NotificationPreference) {
  if (a.telegramEnabled !== b.telegramEnabled) return false;
  if (a.emailEnabled !== b.emailEnabled) return false;
  if (a.webPushEnabled !== b.webPushEnabled) return false;
  if (a.optedOutTypes.length !== b.optedOutTypes.length) return false;
  const sa = [...a.optedOutTypes].sort((x, y) => x - y);
  const sb = [...b.optedOutTypes].sort((x, y) => x - y);
  for (let i = 0; i < sa.length; i++) if (sa[i] !== sb[i]) return false;
  return true;
}

export function NotificationPreferencesSection() {
  const { preferences, isLoading, error } = useNotificationPreferences();
  const { updatePreferences, isPending } = useUpdateNotificationPreferences();
  const { profile } = useMyProfile();
  const hasTelegramLinked = profile?.hasTelegramLinked ?? false;

  const [draft, setDraft] = useState<NotificationPreference | null>(null);

  const baseline: NotificationPreference = preferences ?? {
    telegramEnabled: false,
    emailEnabled: true,
    webPushEnabled: true,
    optedOutTypes: [],
  };
  const current = draft ?? baseline;
  const isDirty = draft !== null && !equal(draft, baseline);

  const onToggleTelegram = (checked: boolean) => {
    setDraft({ ...current, telegramEnabled: checked });
  };
  const onToggleEmail = (checked: boolean) => {
    setDraft({ ...current, emailEnabled: checked });
  };

  const onToggleType = (type: NotificationType, enabled: boolean) => {
    // enabled = юзер хочет получать этот тип → убираем из opt-out
    // !enabled = юзер выключил этот тип → добавляем в opt-out
    const set = new Set(current.optedOutTypes);
    if (enabled) set.delete(type);
    else set.add(type);
    setDraft({ ...current, optedOutTypes: [...set].sort((a, b) => a - b) });
  };

  const onSubmit = () => {
    if (!draft) return;
    updatePreferences(draft);
    setDraft(null);
  };

  const onReset = () => setDraft(null);

  if (isLoading) {
    return (
      <div className="flex items-center justify-center py-8">
        <Icons.loading size={18} className="animate-spin text-muted-foreground" />
      </div>
    );
  }

  if (error) {
    return (
      <div className="flex items-center gap-2 text-sm text-destructive">
        <Icons.error size={16} />
        Не удалось загрузить настройки уведомлений
      </div>
    );
  }

  return (
    <TooltipProvider delayDuration={150}>
      <div className="space-y-4">
        <div className="space-y-3">
          <p className="text-sm text-muted-foreground">
            Уведомления на сайте приходят всегда. Выберите, куда ещё хотите их получать.
          </p>

          <div className="flex items-center justify-between py-3 border-b">
            <div>
              <p className="text-sm font-medium">Telegram</p>
              <p className="text-xs text-muted-foreground mt-0.5">
                {hasTelegramLinked
                  ? "Короткие сообщения прямо в бот"
                  : "Привяжите Telegram в разделе «Связанные аккаунты»"}
              </p>
            </div>
            <Tooltip>
              <TooltipTrigger asChild>
                <span className="inline-flex">
                  <Switch
                    checked={hasTelegramLinked ? current.telegramEnabled : false}
                    disabled={!hasTelegramLinked || isPending}
                    onCheckedChange={hasTelegramLinked ? onToggleTelegram : undefined}
                    aria-label="Уведомления в Telegram"
                  />
                </span>
              </TooltipTrigger>
              {!hasTelegramLinked && (
                <TooltipContent>
                  Привяжите Telegram, чтобы включить уведомления
                </TooltipContent>
              )}
            </Tooltip>
          </div>

          <div className="flex items-center justify-between py-3">
            <div>
              <p className="text-sm font-medium">Email</p>
              <p className="text-xs text-muted-foreground mt-0.5">
                Письмо на вашу почту
              </p>
            </div>
            <Switch
              checked={current.emailEnabled}
              disabled={isPending}
              onCheckedChange={onToggleEmail}
              aria-label="Уведомления на email"
            />
          </div>
        </div>

        <Separator />

        <div className="space-y-3">
          <div>
            <p className="text-sm font-medium">Типы уведомлений</p>
            <p className="text-xs text-muted-foreground mt-0.5">
              Отключите чекбокс, если не хотите получать этот тип во всех каналах
              (включая сайт).
            </p>
          </div>

          <ul className="space-y-2">
            {USER_CONFIGURABLE_TYPES.map((type) => {
              const isOptedOut = current.optedOutTypes.includes(type);
              return (
                <li
                  key={type}
                  className="flex items-center justify-between py-1.5"
                >
                  <label
                    htmlFor={`opt-type-${type}`}
                    className="text-sm cursor-pointer flex-1"
                  >
                    {NotificationTypeLabels[type]}
                  </label>
                  <Switch
                    id={`opt-type-${type}`}
                    checked={!isOptedOut}
                    disabled={isPending}
                    onCheckedChange={(checked) => onToggleType(type, checked)}
                    aria-label={`Получать «${NotificationTypeLabels[type]}»`}
                  />
                </li>
              );
            })}
          </ul>
        </div>

        <div className="flex items-center gap-2">
          <Button
            type="button"
            onClick={onSubmit}
            disabled={!isDirty || isPending}
          >
            {isPending ? (
              <Icons.loading size={14} className="mr-2 animate-spin" />
            ) : (
              <Icons.save size={14} className="mr-2" />
            )}
            {isPending ? "Сохранение..." : "Сохранить"}
          </Button>
          {isDirty && !isPending && (
            <Button type="button" variant="ghost" onClick={onReset}>
              Отменить
            </Button>
          )}
        </div>
      </div>
    </TooltipProvider>
  );
}
