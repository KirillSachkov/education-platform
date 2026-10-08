"use client";

import {
  adminOrdersQueryOptions,
  type AdminOrderSummary,
} from "@/entities/access-admin-order";
import type { OrderStatus } from "@/entities/access-order";
import { BillingSettingsCard } from "@/features/billing-admin";
import { routes } from "@/shared/config/routes";
import { Badge } from "@/shared/ui/kit/badge";
import { Button } from "@/shared/ui/kit/button";
import { EmptyState } from "@/shared/ui/kit/empty-state";
import { Input } from "@/shared/ui/kit/input";
import { Icons } from "@/shared/ui/icons";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/shared/ui/kit/select";
import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { useState } from "react";

const STATUS_LABELS: Record<OrderStatus, string> = {
  PENDING: "В ожидании",
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

const STATUS_FILTER_OPTIONS: ReadonlyArray<OrderStatus | "ALL"> = [
  "ALL",
  "PENDING",
  "PAID",
  "FAILED",
  "REFUNDED",
];

export default function AdminPaymentsPage() {
  const [statusFilter, setStatusFilter] = useState<OrderStatus | "ALL">("ALL");
  const [correlationId, setCorrelationId] = useState("");
  const [page, setPage] = useState(1);

  const ordersQuery = useQuery(
    adminOrdersQueryOptions({
      status: statusFilter === "ALL" ? undefined : statusFilter,
      correlationId: correlationId.trim() || undefined,
      page,
      pageSize: 30,
    }),
  );

  const items = ordersQuery.data?.items ?? [];
  const total = ordersQuery.data?.total ?? 0;

  return (
    <div className="mx-auto max-w-6xl space-y-6 px-4 py-8">
      <header className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Платежи</h1>
          <p className="text-sm text-muted-foreground">Заказы пользователей и audit-trail</p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <Input
            value={correlationId}
            onChange={(e) => {
              setCorrelationId(e.target.value);
              setPage(1);
            }}
            placeholder="Поиск по correlation_id"
            aria-label="Поиск по correlation id"
            spellCheck={false}
            autoComplete="off"
            className="w-full font-mono text-xs sm:w-72"
          />
          <Select
            value={statusFilter}
            onValueChange={(v) => {
              setStatusFilter(v as OrderStatus | "ALL");
              setPage(1);
            }}
          >
            <SelectTrigger className="w-44">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {STATUS_FILTER_OPTIONS.map((s) => (
                <SelectItem key={s} value={s}>
                  {s === "ALL" ? "Все статусы" : STATUS_LABELS[s]}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
      </header>

      <BillingSettingsCard />

      {ordersQuery.isLoading ? (
        <div className="space-y-2">
          {[1, 2, 3, 4].map((i) => (
            <div key={i} className="h-14 rounded-lg bg-muted/40 animate-pulse" />
          ))}
        </div>
      ) : ordersQuery.isError ? (
        <EmptyState
          variant="card"
          icon={Icons.error}
          title="Не удалось загрузить заказы"
          description="Попробуйте перезагрузить страницу."
        />
      ) : items.length === 0 ? (
        <EmptyState
          variant="card"
          icon={Icons.attachment}
          title="Заказов нет"
          description="С этими фильтрами заказы не найдены."
        />
      ) : (
        <div className="space-y-2">
          <div className="grid grid-cols-[1fr_auto_auto_auto_auto] gap-4 px-3 py-2 text-xs text-muted-foreground border-b border-border/40">
            <span>Заказ / юзер</span>
            <span className="hidden sm:inline">Сумма</span>
            <span>Статус</span>
            <span className="hidden md:inline">Создан</span>
            <span>{}</span>
          </div>
          {items.map((order) => (
            <OrderRow key={order.orderId} order={order} />
          ))}
          <Pagination page={page} pageSize={30} total={total} onChange={setPage} />
        </div>
      )}
    </div>
  );
}

function OrderRow({ order }: { order: AdminOrderSummary }) {
  return (
    <div className="grid grid-cols-[1fr_auto_auto_auto_auto] items-center gap-4 rounded-lg border border-border/40 bg-card/40 px-3 py-3">
      <div className="min-w-0">
        <p className="text-sm font-medium truncate" title={order.orderId}>
          {order.orderId.slice(0, 8)}
        </p>
        <p className="text-xs text-muted-foreground truncate" title={order.userId}>
          user · {order.userId.slice(0, 8)}
        </p>
      </div>
      <span className="hidden sm:inline text-sm tabular-nums">
        {formatPrice(order.amountCents, order.currency)}
      </span>
      <Badge variant={STATUS_VARIANTS[order.status]} className="font-normal">
        {STATUS_LABELS[order.status]}
      </Badge>
      <span className="hidden md:inline text-xs text-muted-foreground tabular-nums">
        {formatDate(order.createdAt)}
      </span>
      <Button asChild size="sm" variant="ghost">
        <Link href={routes.adminPaymentDetail(order.orderId)}>
          Детали
          <Icons.chevronRight className="size-4" />
        </Link>
      </Button>
    </div>
  );
}

function Pagination({
  page,
  pageSize,
  total,
  onChange,
}: {
  page: number;
  pageSize: number;
  total: number;
  onChange: (page: number) => void;
}) {
  const totalPages = Math.max(1, Math.ceil(total / pageSize));
  if (totalPages <= 1) return null;
  return (
    <div className="flex items-center justify-between pt-4 text-sm text-muted-foreground">
      <span>
        Страница {page} из {totalPages} · всего {total}
      </span>
      <div className="flex gap-2">
        <Button
          variant="outline"
          size="sm"
          disabled={page <= 1}
          onClick={() => onChange(page - 1)}
        >
          ←
        </Button>
        <Button
          variant="outline"
          size="sm"
          disabled={page >= totalPages}
          onClick={() => onChange(page + 1)}
        >
          →
        </Button>
      </div>
    </div>
  );
}

function formatPrice(priceCents: number, currency: string): string {
  const value = priceCents / 100;
  const symbol = currency === "RUB" ? "₽" : currency;
  return `${value.toLocaleString("ru-RU")} ${symbol}`;
}

function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString("ru-RU", { day: "numeric", month: "short" });
}
