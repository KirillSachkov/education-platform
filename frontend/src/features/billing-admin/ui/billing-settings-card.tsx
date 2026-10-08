"use client";

import { billingConfigQueryOptions } from "@/entities/billing-config";
import { Switch } from "@/shared/ui/kit/switch";
import { useQuery } from "@tanstack/react-query";
import { useUpdateBillingConfig } from "../model/use-update-billing-config";

/**
 * Админ-тоггл приёма прямой оплаты (T-Bank). Рантайм-настройка из AccessService:
 * включено → на тарифах кнопка «Оплатить» (картой); выключено → оформление через Telegram.
 */
export function BillingSettingsCard() {
  const { data, isLoading } = useQuery(billingConfigQueryOptions());
  const { mutate, isPending } = useUpdateBillingConfig();
  const enabled = data?.isEnabled ?? false;

  return (
    <section className="rounded-lg border border-border/40 bg-card/40 p-4">
      <div className="flex items-start justify-between gap-4">
        <div className="min-w-0">
          <h2 className="text-sm font-semibold">Приём прямой оплаты (T-Bank)</h2>
          <p className="mt-1 max-w-prose text-xs text-muted-foreground">
            Включено — на странице тарифов показывается кнопка «Оплатить» с оплатой картой через
            T-Bank. Выключено — оформление идёт через Telegram автора.
          </p>
        </div>
        <Switch
          checked={enabled}
          disabled={isLoading || isPending}
          onCheckedChange={(checked) => mutate(checked)}
          aria-label="Приём прямой оплаты"
        />
      </div>
    </section>
  );
}
