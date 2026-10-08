"use client";

import { Switch } from "@/shared/ui/kit/switch";
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from "@/shared/ui/kit/tooltip";
import { usePushSubscription } from "../model/use-push-subscription";

/**
 * Тумблер push-уведомлений на ТЕКУЩЕМ устройстве (#342). Подписка per-device:
 * включение запрашивает разрешение + регистрирует подписку браузера, выключение —
 * удаляет её. Глобальный backend-флаг `webPushEnabled` остаётся источником правды
 * для общего отключения канала.
 */
export function PushDeviceToggle() {
  const { isSupported, permission, isSubscribed, isPending, subscribe, unsubscribe } =
    usePushSubscription();

  const blocked = permission === "denied";
  const disabled = !isSupported || blocked || isPending;

  const hint = !isSupported
    ? "Откройте установленное приложение (PWA), чтобы включить push"
    : blocked
      ? "Уведомления заблокированы в настройках браузера"
      : null;

  const onToggle = (checked: boolean) => {
    if (checked) void subscribe();
    else void unsubscribe();
  };

  return (
    <TooltipProvider delayDuration={150}>
      <div className="flex items-center justify-between py-3 border-b">
        <div>
          <p className="text-sm font-medium">Push на этом устройстве</p>
          <p className="text-xs text-muted-foreground mt-0.5">
            {hint ?? "Уведомления на экран телефона, даже когда вкладка закрыта"}
          </p>
        </div>
        <Tooltip>
          <TooltipTrigger asChild>
            <span className="inline-flex">
              <Switch
                checked={isSubscribed}
                disabled={disabled}
                onCheckedChange={onToggle}
                aria-label="Push-уведомления на этом устройстве"
              />
            </span>
          </TooltipTrigger>
          {hint && <TooltipContent>{hint}</TooltipContent>}
        </Tooltip>
      </div>
    </TooltipProvider>
  );
}
