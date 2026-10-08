"use client";
import { Icons } from "@/shared/ui/icons";

import { useQuery } from "@tanstack/react-query";
import { adminCrossServiceQueryOptions } from "@/entities/admin-cross-service";
import { Badge } from "@/shared/ui/kit/badge";
import { Card, CardContent } from "@/shared/ui/kit/card";
import { EmptyState } from "@/shared/ui/kit/empty-state";
import { Skeleton } from "@/shared/ui/kit/skeleton";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/shared/ui/kit/table";
import { formatCents, formatDate } from "../../lib/format";

type PaymentsTabProps = {
  userId: string;
};

export function PaymentsTab({ userId }: PaymentsTabProps) {
  const query = useQuery(adminCrossServiceQueryOptions.getOrdersOptions(userId));

  if (query.isLoading) return <Skeletons />;
  if (query.isError) return <EmptyState variant="card" title="Не удалось загрузить заказы" icon={Icons.list} />;

  const items = query.data ?? [];
  if (items.length === 0) return <EmptyState variant="card" title="Нет заказов" icon={Icons.list} />;

  return (
    <Card>
      <CardContent className="p-0">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>План</TableHead>
              <TableHead>Сумма</TableHead>
              <TableHead>Status</TableHead>
              <TableHead>Provider</TableHead>
              <TableHead>Создан</TableHead>
              <TableHead>Оплачен</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {items.map((o) => (
              <TableRow key={o.id}>
                <TableCell>{o.planDisplayName ?? <code className="text-xs">{o.planId}</code>}</TableCell>
                <TableCell className="font-medium tabular-nums">
                  {formatCents(o.amountCents, o.currency)}
                </TableCell>
                <TableCell>
                  <Badge variant={statusVariant(o.status)}>{o.status}</Badge>
                </TableCell>
                <TableCell>
                  <Badge variant="outline" className="text-xs">
                    {o.provider}
                  </Badge>
                </TableCell>
                <TableCell className="text-sm">{formatDate(o.createdAt)}</TableCell>
                <TableCell className="text-sm">{formatDate(o.paidAt)}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </CardContent>
    </Card>
  );
}

function statusVariant(status: string): "default" | "secondary" | "destructive" | "outline" {
  if (status === "PAID") return "secondary";
  if (status === "FAILED") return "destructive";
  if (status === "PENDING") return "outline";
  return "default";
}

function Skeletons() {
  return (
    <div className="space-y-2">
      {[1, 2, 3].map((i) => (
        <Skeleton key={i} className="h-12 w-full" />
      ))}
    </div>
  );
}
