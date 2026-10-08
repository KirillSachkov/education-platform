"use client";

import {
  adminOrderDetailQueryOptions,
  adminOrdersApi,
  type AdminOrderEventDto,
} from "@/entities/access-admin-order";
import type { OrderStatus } from "@/entities/access-order";
import { getErrorMessage } from "@/shared/api";
import { routes } from "@/shared/config/routes";
import { Badge } from "@/shared/ui/kit/badge";
import { Button } from "@/shared/ui/kit/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/ui/kit/card";
import { cn } from "@/shared/lib/css";
import { Icons } from "@/shared/ui/icons";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { useParams } from "next/navigation";
import { useRef, useState } from "react";
import { toast } from "sonner";

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

/** RU labels + icons + цветовой тон для типов OrderEvent (#443). */
const ORDER_EVENT_META: Record<string, { label: string; icon: keyof typeof Icons; tone: string }> = {
  INIT_CALLED: { label: "Init: запрос отправлен", icon: "send", tone: "text-foreground" },
  INIT_RESPONDED: { label: "Init: успех", icon: "success", tone: "text-emerald-600" },
  INIT_FAILED: { label: "Init: ошибка", icon: "error", tone: "text-destructive" },
  INIT_URL_REJECTED: { label: "Init: недоверенный URL", icon: "shieldAlert", tone: "text-amber-600" },
  WEBHOOK_RECEIVED: { label: "Webhook получен", icon: "arrowSwap", tone: "text-foreground" },
  WEBHOOK_REJECTED_INVALID_TOKEN: {
    label: "Webhook: неверная подпись",
    icon: "shieldAlert",
    tone: "text-amber-600",
  },
  WEBHOOK_REJECTED_AMOUNT_MISMATCH: {
    label: "Webhook: сумма не совпала",
    icon: "warning",
    tone: "text-amber-600",
  },
  WEBHOOK_REJECTED_REPLAY: { label: "Webhook: повтор (replay)", icon: "shieldAlert", tone: "text-amber-600" },
  MARK_PAID: { label: "Оплачен", icon: "creditCard", tone: "text-emerald-600" },
  MARK_FAILED: { label: "Не оплачен", icon: "error", tone: "text-destructive" },
  GRANT_ISSUED: { label: "Доступ выдан", icon: "unlocked", tone: "text-emerald-600" },
  GRANT_FAILED: { label: "Доступ не выдан", icon: "warning", tone: "text-destructive" },
  REFUNDED: { label: "Возврат", icon: "undo", tone: "text-foreground" },
  PARTIAL_REFUND_IGNORED: {
    label: "Частичный возврат (проигнорирован)",
    icon: "info",
    tone: "text-muted-foreground",
  },
  RECONCILIATION_RECOVERED: { label: "Восстановлен (reconciliation)", icon: "refresh", tone: "text-foreground" },
  MANUAL_GRANT_BY_ADMIN: { label: "Доступ выдан админом", icon: "unlocked", tone: "text-emerald-600" },
  MANUAL_REVOKE_BY_ADMIN: { label: "Доступ отозван админом", icon: "locked", tone: "text-destructive" },
};

export default function AdminPaymentDetailPage() {
  const params = useParams<{ id: string }>();
  const orderId = params.id;
  const queryClient = useQueryClient();

  const detailQuery = useQuery(adminOrderDetailQueryOptions(orderId));

  const resyncMutation = useMutation({
    mutationFn: () => adminOrdersApi.resync(orderId),
    onSuccess: (envelope) => {
      const result = envelope.result;
      if (result) {
        toast.success(
          `Resync ок: ${STATUS_LABELS[result.previousStatus]} → ${STATUS_LABELS[result.newStatus]}`,
        );
      } else {
        toast.success("Resync выполнен");
      }
      queryClient.invalidateQueries({ queryKey: ["access-admin-orders", orderId] });
    },
    onError: (error) => toast.error(getErrorMessage(error, "Не удалось выполнить resync")),
  });

  if (detailQuery.isLoading) {
    return (
      <div className="mx-auto mt-12 max-w-3xl px-4">
        <div className="h-12 w-2/3 rounded bg-muted animate-pulse" />
        <div className="mt-6 h-64 rounded bg-muted animate-pulse" />
      </div>
    );
  }

  if (detailQuery.isError || !detailQuery.data) {
    return (
      <div className="mx-auto mt-24 max-w-md px-4 text-center">
        <h2 className="text-xl font-semibold">Заказ не найден</h2>
        <Button asChild variant="outline" className="mt-6">
          <Link href={routes.adminPayments}>К списку платежей</Link>
        </Button>
      </div>
    );
  }

  const { order, events } = detailQuery.data;

  return (
    <div className="mx-auto max-w-3xl space-y-6 px-4 py-8">
      <Link
        href={routes.adminPayments}
        className="inline-flex items-center gap-2 text-sm text-muted-foreground hover:underline"
      >
        <Icons.back className="size-4" />К платежам
      </Link>

      <header className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">
            Заказ {order.orderId.slice(0, 8)}
          </h1>
          <p className="mt-1 text-sm text-muted-foreground tabular-nums">
            {formatPrice(order.amountCents, order.currency)} · создан {formatDateTime(order.createdAt)}
          </p>
        </div>
        <Badge variant={STATUS_VARIANTS[order.status]}>{STATUS_LABELS[order.status]}</Badge>
      </header>

      <Card>
        <CardHeader>
          <CardTitle className="text-base">Поля</CardTitle>
        </CardHeader>
        <CardContent className="grid grid-cols-1 sm:grid-cols-2 gap-4 text-sm">
          <Field label="Order ID" value={order.orderId} mono />
          <Field label="User ID" value={order.userId} mono />
          <Field label="Plan ID" value={order.planId} mono />
          <Field label="Provider" value={order.provider ?? "—"} />
          <Field label="External ref" value={order.externalProviderRef ?? "—"} mono />
          <Field
            label="Оплачен"
            value={order.paidAt ? formatDateTime(order.paidAt) : "—"}
          />
          {order.failureReason ? (
            <Field
              label="Причина отказа"
              value={order.failureReason}
              className="sm:col-span-2"
            />
          ) : null}
          {order.correlationId ? (
            <div className="sm:col-span-2">
              <p className="text-xs text-muted-foreground">Correlation ID (trace)</p>
              <div className="mt-0.5 flex items-center gap-2">
                <span className="break-all font-mono text-xs">{order.correlationId}</span>
                <CopyButton value={order.correlationId} label="correlation id" />
              </div>
            </div>
          ) : null}
        </CardContent>
      </Card>

      <Card>
        <CardHeader className="flex flex-row items-center justify-between">
          <CardTitle className="text-base">Действия</CardTitle>
        </CardHeader>
        <CardContent className="flex flex-wrap gap-2">
          <Button
            type="button"
            variant="outline"
            size="sm"
            disabled={resyncMutation.isPending || order.status !== "PENDING"}
            onClick={() => resyncMutation.mutate()}
          >
            {resyncMutation.isPending ? (
              <>
                <Icons.loading className="size-4 animate-spin" />
                Resync…
              </>
            ) : (
              <>Resync через T-Bank</>
            )}
          </Button>
          <Button type="button" variant="outline" size="sm" disabled title="В разработке (Phase 7)">
            Возврат
          </Button>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="text-base">Audit timeline</CardTitle>
        </CardHeader>
        <CardContent className="space-y-2">
          {events.length === 0 ? (
            <p className="text-sm text-muted-foreground">События отсутствуют.</p>
          ) : (
            events.map((event) => <EventRow key={event.id} event={event} />)
          )}
        </CardContent>
      </Card>
    </div>
  );
}

function EventRow({ event }: { event: AdminOrderEventDto }) {
  const meta = ORDER_EVENT_META[event.eventType];
  const Icon = Icons[meta?.icon ?? "info"];
  return (
    <div className="rounded-lg border border-border/40 bg-card/40 p-3">
      <div className="flex items-center justify-between gap-2">
        <span className={cn("inline-flex items-center gap-2 text-sm font-medium", meta?.tone)}>
          <Icon className="size-4 shrink-0" />
          {meta?.label ?? event.eventType}
        </span>
        <span className="text-xs text-muted-foreground tabular-nums">
          {formatDateTime(event.createdAt)}
        </span>
      </div>
      <p className="mt-1 font-mono text-[10px] uppercase tracking-wide text-muted-foreground/70">
        {event.eventType}
      </p>
      {event.actorUserId ? (
        <p className="mt-1 text-xs text-muted-foreground">
          actor: <span className="font-mono">{event.actorUserId.slice(0, 8)}</span>
        </p>
      ) : null}
      {event.correlationId ? (
        <p className="mt-1 flex items-center gap-1.5 text-xs text-muted-foreground">
          trace: <span className="break-all font-mono">{event.correlationId}</span>
          <CopyButton value={event.correlationId} label="correlation id" />
        </p>
      ) : null}
      {event.payloadJson ? (
        <pre className="mt-2 max-h-40 overflow-auto rounded bg-muted/40 p-2 text-xs font-mono">
          {tryPrettyJson(event.payloadJson)}
        </pre>
      ) : null}
    </div>
  );
}

function CopyButton({ value, label }: { value: string; label: string }) {
  const [copied, setCopied] = useState(false);
  const timeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  const handleCopy = () => {
    navigator.clipboard.writeText(value).catch(() => {});
    setCopied(true);
    if (timeoutRef.current) clearTimeout(timeoutRef.current);
    timeoutRef.current = setTimeout(() => setCopied(false), 2000);
  };

  return (
    <button
      type="button"
      onClick={handleCopy}
      aria-label={copied ? `${label} скопирован` : `Скопировать ${label}`}
      title={copied ? "Скопировано" : "Скопировать"}
      className="inline-flex shrink-0 items-center text-muted-foreground transition-colors hover:text-foreground"
    >
      {copied ? (
        <Icons.check className="size-3.5 text-emerald-600" />
      ) : (
        <Icons.copy className="size-3.5" />
      )}
    </button>
  );
}

function Field({
  label,
  value,
  mono,
  className,
}: {
  label: string;
  value: string;
  mono?: boolean;
  className?: string;
}) {
  return (
    <div className={className}>
      <p className="text-xs text-muted-foreground">{label}</p>
      <p className={mono ? "mt-0.5 break-all font-mono text-xs" : "mt-0.5 text-sm"}>{value}</p>
    </div>
  );
}

function formatPrice(priceCents: number, currency: string): string {
  const value = priceCents / 100;
  const symbol = currency === "RUB" ? "₽" : currency;
  return `${value.toLocaleString("ru-RU")} ${symbol}`;
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

function tryPrettyJson(raw: string): string {
  try {
    return JSON.stringify(JSON.parse(raw), null, 2);
  } catch {
    return raw;
  }
}
