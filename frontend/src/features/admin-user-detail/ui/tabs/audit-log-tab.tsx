"use client";
import { Icons } from "@/shared/ui/icons";

import { useQuery } from "@tanstack/react-query";
import { usersQueryOptions } from "@/entities/user";
import { Badge } from "@/shared/ui/kit/badge";
import { Card, CardContent } from "@/shared/ui/kit/card";
import { EmptyState } from "@/shared/ui/kit/empty-state";
import { Skeleton } from "@/shared/ui/kit/skeleton";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/shared/ui/kit/table";
import { formatDate } from "../../lib/format";

type AuditLogTabProps = {
  userId: string;
};

export function AuditLogTab({ userId }: AuditLogTabProps) {
  const query = useQuery(usersQueryOptions.getAdminAuditLogOptions({ targetUserId: userId, pageSize: 50 }));

  if (query.isLoading) return <Skeletons />;
  if (query.isError) return <EmptyState variant="card" title="Не удалось загрузить лог админ-действий" icon={Icons.list} />;

  const items = query.data?.items ?? [];
  if (items.length === 0)
    return <EmptyState variant="card" title="Админ-действий над этим юзером не было" icon={Icons.list} />;

  return (
    <Card>
      <CardContent className="p-0">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Когда</TableHead>
              <TableHead>Админ</TableHead>
              <TableHead>Действие</TableHead>
              <TableHead>Method</TableHead>
              <TableHead>Результат</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {items.map((entry) => (
              <TableRow key={entry.id}>
                <TableCell className="text-sm">{formatDate(entry.createdAt)}</TableCell>
                <TableCell>
                  {entry.adminUsername ?? entry.adminDisplayName ?? <code className="text-xs">{entry.adminId}</code>}
                </TableCell>
                <TableCell>
                  <Badge variant="outline" className="text-xs">
                    {entry.action}
                  </Badge>
                </TableCell>
                <TableCell className="font-mono text-xs">{entry.method}</TableCell>
                <TableCell>
                  {entry.result === "success" ? (
                    <Badge variant="secondary">success</Badge>
                  ) : (
                    <Badge variant="destructive" title={entry.errorMessage ?? undefined}>
                      failure
                    </Badge>
                  )}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </CardContent>
    </Card>
  );
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
