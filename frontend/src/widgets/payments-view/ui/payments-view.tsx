"use client";

import {
  myOrdersQueryOptions,
  type MeOrderSummary,
  type OrderStatus,
} from "@/entities/access-order";
import { formatPriceFromCents } from "@/entities/access-plan";
import { routes } from "@/shared/config/routes";
import { Badge } from "@/shared/ui/kit/badge";
import { Button } from "@/shared/ui/kit/button";
import { EmptyState } from "@/shared/ui/kit/empty-state";
import { Icons } from "@/shared/ui/icons";
import { useQuery } from "@tanstack/react-query";
import Link from "next/link";

const STATUS_LABELS: Record<OrderStatus, string> = {
  PENDING: "Ожидает оплаты",
  PAID: "Оплачен",
  FAILED: "Не оплачен",
  REFUNDED: "Возвращён",
};

const STATUS_VARIANTS: Record<
  OrderStatus,
  "default" | "secondary" | "destructive" | "outline"
> = {
  PENDING: "secondary",
  PAID: "default",
  FAILED: "destructive",
  REFUNDED: "outline",
};


/**
 * `/payments` — подробная история заказов текущего юзера: статус, сумма, дата,
 * название плана, причина отказа человеческим текстом. Чеки приходят на email
 * автоматически (онлайн-касса T-Bank). Промоут из `/settings/payments` (#414).
 */
export function PaymentsView() {
  const ordersQuery = useQuery(myOrdersQueryOptions({ pageSize: 50 }));
  // Название плана приходит прямо в заказе (planTitle, #512) — полный публичный
  // каталог планов ради display name'ов больше не тянем.
  const orders = ordersQuery.data?.items ?? [];

  return (
    <div className="mx-auto w-full max-w-3xl px-4 sm:px-6 py-6 sm:py-10">
      <header className="mb-6 sm:mb-8 space-y-1.5">
        <h1 className="text-2xl sm:text-3xl font-semibold tracking-tight">Платежи</h1>
        <p className="text-sm text-muted-foreground">
          История заказов на платные планы: статусы, суммы и причины отказов.
        </p>
      </header>

      {ordersQuery.isLoading ? (
        <div className="space-y-3">
          {[1, 2, 3].map((i) => (
            <div key={i} className="h-24 rounded-2xl bg-muted/40 animate-pulse" />
          ))}
        </div>
      ) : ordersQuery.isError ? (
        <EmptyState
          variant="card"
          icon={Icons.error}
          title="Не удалось загрузить платежи"
          description="Перезагрузите страницу или зайдите позже."
        />
      ) : orders.length === 0 ? (
        <EmptyState
          variant="card"
          icon={Icons.creditCard}
          title="Платежей пока нет"
          description="Здесь появятся ваши заказы на платные планы. Чек на email отправит онлайн-касса автоматически."
          action={
            <Button asChild>
              <Link href={routes.pricing}>Посмотреть планы</Link>
            </Button>
          }
        />
      ) : (
        <div className="space-y-3">
          <p className="flex items-start gap-2 rounded-xl border border-border/50 bg-muted/30 px-3 py-2.5 text-xs text-muted-foreground">
            <Icons.info className="mt-0.5 size-3.5 shrink-0" />
            <span>
              Чек на email отправляет онлайн-касса T-Bank автоматически после успешной оплаты. Если
              чек не пришёл — проверьте папку «Спам».
            </span>
          </p>
          {orders.map((order) => (
            <OrderCard key={order.orderId} order={order} planName={order.planTitle} />
          ))}
        </div>
      )}
    </div>
  );
}

function OrderCard({ order, planName }: { order: MeOrderSummary; planName: string | null }) {
  return (
    <article className="rounded-2xl border border-border/60 bg-card/60 p-4 sm:p-5">
      <header className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0 flex-1">
          <div className="flex flex-wrap items-center gap-2">
            <h2 className="text-base font-semibold leading-tight">{planName ?? "План"}</h2>
            <Badge variant={STATUS_VARIANTS[order.status]} className="font-normal">
              {STATUS_LABELS[order.status]}
            </Badge>
          </div>
          <p className="mt-1 text-sm text-foreground/90 tabular-nums">
            {formatPriceFromCents(order.amountCents, order.currency)}
          </p>
        </div>
      </header>

      <dl className="mt-3 grid grid-cols-1 gap-x-6 gap-y-1.5 text-xs sm:grid-cols-2">
        <Row label="Создан" value={formatDateTime(order.createdAt)} />
        {order.paidAt ? <Row label="Оплачен" value={formatDateTime(order.paidAt)} /> : null}
        <Row label="Номер заказа" value={order.orderId.slice(0, 8)} mono />
      </dl>

      {(order.status === "FAILED" || order.status === "REFUNDED") && order.failureReason ? (
        <p
          className={
            order.status === "FAILED"
              ? "mt-3 flex items-start gap-2 rounded-lg bg-destructive/10 px-3 py-2 text-xs text-destructive"
              : "mt-3 flex items-start gap-2 rounded-lg bg-muted/40 px-3 py-2 text-xs text-muted-foreground"
          }
        >
          <Icons.warning className="mt-0.5 size-3.5 shrink-0" />
          <span>{order.failureReason}</span>
        </p>
      ) : null}
    </article>
  );
}

function Row({ label, value, mono }: { label: string; value: string; mono?: boolean }) {
  return (
    <div className="flex items-baseline justify-between gap-3 sm:block">
      <dt className="text-muted-foreground">{label}</dt>
      <dd className={mono ? "font-mono text-foreground/90" : "text-foreground/90"}>{value}</dd>
    </div>
  );
}

function formatDateTime(iso: string): string {
  return new Date(iso).toLocaleString("ru-RU", {
    day: "numeric",
    month: "short",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  });
}
