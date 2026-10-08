"use client";

import { orderStatusQueryOptions } from "@/entities/access-order";
import { currentOnboardingQueryOptions } from "@/entities/plan-onboarding";
import { clearLastOrderId, readLastOrderId } from "@/features/buy-plan";
import { trackGrowthEvent } from "@/shared/analytics";
import { routes } from "@/shared/config/routes";
import { clearPricingIntent, readPricingIntent } from "@/shared/lib/pricing-intent";
import { Button } from "@/shared/ui/kit/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/ui/kit/card";
import { Icons } from "@/shared/ui/icons";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { useEffect, useState, useSyncExternalStore } from "react";

const POLLING_TIMEOUT_MS = 30_000;

const NULL_SNAPSHOT = (): string | null => null;
const subscribeNoop = () => () => {};

/**
 * Читает orderId из localStorage. SSR snapshot — null (страница рендерится
 * клиентом). Использует useSyncExternalStore вместо useState+useEffect, чтобы
 * не нарушать react-hooks/set-state-in-effect.
 */
function useStoredOrderId(): string | null {
  return useSyncExternalStore(subscribeNoop, () => readLastOrderId(), NULL_SNAPSHOT);
}

/**
 * `/payment/success` — landing page после успешного редиректа с T-Bank.
 * T-Bank приклеивает `OrderId` к URL'у (см. terminal SuccessURL config).
 * Polling статуса 1 Hz до терминального состояния PAID. Через 30 сек —
 * показываем «процесс занимает дольше обычного, мы пришлём email когда платёж
 * подтвердится».
 *
 * fallback на `localStorage.lastOrderId` — если T-Bank query не приклеил.
 */
export default function PaymentSuccessPage() {
  const searchParams = useSearchParams();
  // T-Bank присылает `OrderId` (PascalCase). Поддерживаем оба регистра.
  const queryOrderId = searchParams.get("OrderId") ?? searchParams.get("orderId");
  const storedOrderId = useStoredOrderId();
  const orderId = queryOrderId ?? storedOrderId;

  const [pollingTimedOut, setPollingTimedOut] = useState(false);
  useEffect(() => {
    if (!orderId) return;
    const timer = setTimeout(() => setPollingTimedOut(true), POLLING_TIMEOUT_MS);
    return () => clearTimeout(timer);
  }, [orderId]);

  const statusQuery = useQuery(orderStatusQueryOptions(orderId));
  const status = statusQuery.data?.status;
  const queryClient = useQueryClient();

  useEffect(() => {
    if (status === "PAID" || status === "FAILED" || status === "REFUNDED") {
      clearLastOrderId();
    }
    // После успешной оплаты онбординг создаётся реактивно
    // (`PlanGrantCreatedOnboardingHandler`). Инвалидируем `current-onboarding`
    // query, чтобы `OnboardingOverlay` в `(app)/layout.tsx` подхватил свежие
    // данные и открыл modal поверх текущей страницы.
    if (status === "PAID") {
      const pricingIntent = readPricingIntent();
      if (pricingIntent) {
        trackGrowthEvent(
          {
            name: "purchase_success",
            properties: {
              plan_id: pricingIntent.planSlug,
              correlation_id: pricingIntent.intentId,
            },
          },
          { once: `pricing-purchase:${pricingIntent.intentId}` },
        );
        clearPricingIntent(pricingIntent.intentId);
      }
      queryClient.invalidateQueries({ queryKey: currentOnboardingQueryOptions.queryKey });
    }
  }, [status, queryClient]);

  // Запрос упал (404 на чужой/устаревший orderId, либо backend down) —
  // показываем нейтральное состояние вместо вечного спиннера.
  const queryFailed = statusQuery.isError;

  // Когда orderId неизвестен или запрос упал — не флешим юзеру loading-spinner.
  // «Что-то пошло не так» — нейтральный кейс. Чаще всего попадают сюда после
  // повторного захода на /payment/success: orderId уже clear'нут (PAID), а в URL
  // его нет (T-Bank редирект отработал в прошлый раз).
  const isUnknown = !orderId || queryFailed;

  return (
    <div className="mx-auto mt-16 max-w-md px-4">
      <Card>
        <CardHeader className="items-center text-center">
          {isUnknown ? (
            <>
              <span className="flex size-14 items-center justify-center rounded-full bg-muted text-muted-foreground">
                <Icons.help className="size-8" />
              </span>
              <CardTitle className="mt-4 text-2xl">Заказ не найден</CardTitle>
            </>
          ) : status === "PAID" ? (
            <>
              <span className="flex size-14 items-center justify-center rounded-full bg-emerald-500/10 text-emerald-500">
                <Icons.completed className="size-8" />
              </span>
              <CardTitle className="mt-4 text-2xl">Доступ активирован</CardTitle>
            </>
          ) : status === "FAILED" || status === "REFUNDED" ? (
            <>
              <span className="flex size-14 items-center justify-center rounded-full bg-destructive/10 text-destructive">
                <Icons.error className="size-8" />
              </span>
              <CardTitle className="mt-4 text-2xl">Что-то пошло не так</CardTitle>
            </>
          ) : (
            <>
              <Icons.loading className="size-12 animate-spin text-primary" />
              <CardTitle className="mt-4 text-2xl">Подтверждаем оплату…</CardTitle>
            </>
          )}
        </CardHeader>
        <CardContent className="space-y-4 text-center">
          {isUnknown ? (
            <>
              <p className="text-sm text-muted-foreground">
                Идентификатор заказа потерян или ссылка устарела. Если вы только что оплатили — план
                уже выдан, проверьте раздел «Мои планы».
              </p>
              <Button asChild className="w-full">
                <Link href={routes.settings}>К моим планам</Link>
              </Button>
            </>
          ) : status === "PAID" ? (
            <>
              <p className="text-sm text-muted-foreground">
                План доступа выдан. Чек об оплате уже летит на ваш email.
              </p>
              <Button asChild className="w-full">
                <Link href={routes.home}>Перейти к обучению</Link>
              </Button>
            </>
          ) : status === "FAILED" || status === "REFUNDED" ? (
            <>
              <p className="text-sm text-muted-foreground">
                Платёж не прошёл. Если деньги списаны — они вернутся на карту в течение нескольких
                рабочих дней.
              </p>
              {statusQuery.data?.failureReason ? (
                <p className="text-xs text-muted-foreground">
                  Причина: {statusQuery.data.failureReason}
                </p>
              ) : null}
              <Button asChild variant="outline" className="w-full">
                <Link href={routes.pricing}>Попробовать снова</Link>
              </Button>
            </>
          ) : pollingTimedOut ? (
            <>
              <p className="text-sm text-muted-foreground">
                Обработка занимает дольше обычного. Если платёж пройдёт — мы пришлём чек на email и
                активируем доступ автоматически.
              </p>
              <Button asChild variant="outline" className="w-full">
                <Link href={routes.settings}>К моим планам</Link>
              </Button>
            </>
          ) : (
            <p className="text-sm text-muted-foreground">
              Это занимает обычно несколько секунд. Не закрывайте страницу.
            </p>
          )}
        </CardContent>
      </Card>
    </div>
  );
}
