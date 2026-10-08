"use client";
import { Icons } from "@/shared/ui/icons";

import { useQuery } from "@tanstack/react-query";
import { adminCrossServiceQueryOptions } from "@/entities/admin-cross-service";
import { Badge } from "@/shared/ui/kit/badge";
import { Card, CardContent } from "@/shared/ui/kit/card";
import { EmptyState } from "@/shared/ui/kit/empty-state";
import { Skeleton } from "@/shared/ui/kit/skeleton";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/shared/ui/kit/table";
import { formatDate } from "../../lib/format";

type EnrollmentsTabProps = {
  userId: string;
};

export function EnrollmentsTab({ userId }: EnrollmentsTabProps) {
  const query = useQuery(adminCrossServiceQueryOptions.getEnrollmentsOptions(userId));

  if (query.isLoading) return <TabSkeleton />;
  if (query.isError)
    return <EmptyState variant="card" title="Не удалось загрузить записи на курсы" icon={Icons.list} />;

  const items = query.data ?? [];
  if (items.length === 0)
    return <EmptyState variant="card" title="Нет записей на курсы" icon={Icons.list} />;

  return (
    <Card>
      <CardContent className="p-0">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Курс</TableHead>
              <TableHead>Источник</TableHead>
              <TableHead>Записан</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {items.map((e) => (
              <TableRow key={`${e.courseId}-${e.enrolledAt}`}>
                <TableCell className="font-mono text-xs">{e.courseId}</TableCell>
                <TableCell>
                  <Badge variant="outline" className="text-xs">
                    {e.source}
                  </Badge>
                </TableCell>
                <TableCell className="text-sm">{formatDate(e.enrolledAt)}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </CardContent>
    </Card>
  );
}

function TabSkeleton() {
  return (
    <div className="space-y-2">
      {[1, 2, 3].map((i) => (
        <Skeleton key={i} className="h-12 w-full" />
      ))}
    </div>
  );
}
