"use client";

import { notificationQueryOptions, notificationsApi } from "@/entities/notification";
import { getErrorMessage } from "@/shared/api";
import { Card } from "@/shared/ui/kit/card";
import { Icons } from "@/shared/ui/icons";
import { Switch } from "@/shared/ui/kit/switch";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

/**
 *  Inline-настройка email/telegram уведомлений. Reuses существующий
 *  /notifications/preferences API. InApp всегда on (product-decision из NotificationService).
 */
export function NotificationsStepView() {
  const queryClient = useQueryClient();
  const { data, isPending } = useQuery(notificationQueryOptions.preferences());

  const update = useMutation({
    mutationFn: notificationsApi.updatePreferences,
    onSuccess: async () => {
      toast.success("Настройки сохранены");
      await queryClient.invalidateQueries({
        queryKey: notificationQueryOptions.preferencesKey(),
      });
    },
    onError: (e) => toast.error(getErrorMessage(e, "Не удалось обновить настройки")),
  });

  if (isPending || !data) {
    return <div className="text-muted-foreground">Загрузка настроек…</div>;
  }

  const handleToggle = (channel: "email" | "telegram", checked: boolean) => {
    update.mutate({
      ...data,
      emailEnabled: channel === "email" ? checked : data.emailEnabled,
      telegramEnabled: channel === "telegram" ? checked : data.telegramEnabled,
    });
  };

  return (
    <div className="space-y-4">
      <div className="flex items-center gap-3">
        <Icons.notification className="h-8 w-8 text-primary" />
        <h2 className="text-2xl font-semibold">Настройка уведомлений</h2>
      </div>
      <p className="text-muted-foreground">
        Выбери каналы для уведомлений. Можно изменить в любой момент в «Настройки → Уведомления».
      </p>
      <Card className="space-y-3 p-4">
        <ToggleRow
          title="Email"
          description="На почту приходят сводки и важные оповещения."
          checked={data.emailEnabled}
          disabled={update.isPending}
          onCheckedChange={(checked) => handleToggle("email", checked)}
        />
        <ToggleRow
          title="Telegram"
          description="Бот шлёт быстрые уведомления (если ты привязал Telegram)."
          checked={data.telegramEnabled}
          disabled={update.isPending}
          onCheckedChange={(checked) => handleToggle("telegram", checked)}
        />
      </Card>
    </div>
  );
}

type ToggleRowProps = {
  title: string;
  description: string;
  checked: boolean;
  disabled: boolean;
  onCheckedChange: (checked: boolean) => void;
};

function ToggleRow({ title, description, checked, disabled, onCheckedChange }: ToggleRowProps) {
  return (
    <div className="flex items-center justify-between gap-4">
      <div>
        <div className="font-medium">{title}</div>
        <p className="text-sm text-muted-foreground">{description}</p>
      </div>
      <Switch checked={checked} onCheckedChange={onCheckedChange} disabled={disabled} />
    </div>
  );
}
