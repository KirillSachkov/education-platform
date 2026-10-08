"use client";

import type { AdminPostPurchaseOrder } from "@/entities/admin-cross-service";
import { Badge } from "@/shared/ui/kit/badge";
import { Card, CardContent } from "@/shared/ui/kit/card";
import { EmptyState } from "@/shared/ui/kit/empty-state";
import { Icons } from "@/shared/ui/icons";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/shared/ui/kit/table";
import { formatCents, formatDate } from "../lib/format";

type PaidOrdersProps = {
  orders: AdminPostPurchaseOrder[];
};

/**
 * Заказы юзера (#444). Dual-render: desktop `<Table>` + mobile карточки (тот же
 * массив), как в grants-tab/payments-tab. Сумма берётся из контракта (RUB).
 */
export function PaidOrders({ orders }: PaidOrdersProps) {
  if (orders.length === 0) {
    return <EmptyState variant="card" title="Нет заказов" icon={Icons.creditCard} />;
  }

  return (
    <>
      {/* Desktop table */}
      <Card className="hidden md:block">
        <CardContent className="p-0">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>План</TableHead>
                <TableHead>Сумма</TableHead>
                <TableHead>Статус</TableHead>
                <TableHead>Создан</TableHead>
                <TableHead>Оплачен</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {orders.map((o) => (
                <TableRow key={o.id}>
                  <TableCell>
                    {o.planDisplayName ?? <code className="text-xs">{o.planId}</code>}
                  </TableCell>
                  <TableCell className="font-medium tabular-nums">
                    {formatCents(o.amountCents)}
                  </TableCell>
                  <TableCell>
                    <Badge variant={statusVariant(o.status)}>{o.status}</Badge>
                  </TableCell>
                  <TableCell className="text-sm">{formatDate(o.createdAt)}</TableCell>
                  <TableCell className="text-sm">{formatDate(o.paidAt)}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </CardContent>
      </Card>

      {/* Mobile stacked cards */}
      <div className="space-y-3 md:hidden">
        {orders.map((o) => (
          <Card key={o.id}>
            <CardContent className="space-y-3 p-4">
              <div className="flex items-start justify-between gap-2">
                <p className="min-w-0 text-sm font-medium break-words">
                  {o.planDisplayName ?? <code className="text-xs">{o.planId}</code>}
                </p>
                <Badge variant={statusVariant(o.status)}>{o.status}</Badge>
              </div>
              <p className="text-base font-semibold tabular-nums">{formatCents(o.amountCents)}</p>
              <dl className="grid grid-cols-2 gap-2 text-xs text-muted-foreground">
                <div>
                  <dt>Создан</dt>
                  <dd className="text-foreground/90">{formatDate(o.createdAt)}</dd>
                </div>
                <div>
                  <dt>Оплачен</dt>
                  <dd className="text-foreground/90">{formatDate(o.paidAt)}</dd>
                </div>
              </dl>
            </CardContent>
          </Card>
        ))}
      </div>
    </>
  );
}

function statusVariant(status: string): "default" | "secondary" | "destructive" | "outline" {
  if (status === "PAID") return "secondary";
  if (status === "FAILED") return "destructive";
  if (status === "PENDING") return "outline";
  return "default";
}
