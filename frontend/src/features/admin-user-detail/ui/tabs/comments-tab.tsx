"use client";
import { Icons } from "@/shared/ui/icons";

import { useQuery } from "@tanstack/react-query";
import { adminCrossServiceQueryOptions } from "@/entities/admin-cross-service";
import { Badge } from "@/shared/ui/kit/badge";
import { Card, CardContent } from "@/shared/ui/kit/card";
import { EmptyState } from "@/shared/ui/kit/empty-state";
import { Skeleton } from "@/shared/ui/kit/skeleton";
import { formatDate } from "../../lib/format";

type CommentsTabProps = {
  userId: string;
};

export function CommentsTab({ userId }: CommentsTabProps) {
  const query = useQuery(adminCrossServiceQueryOptions.getRecentCommentsOptions(userId));

  if (query.isLoading) return <Skeletons />;
  if (query.isError) return <EmptyState variant="card" title="Не удалось загрузить комментарии" icon={Icons.list} />;

  const items = query.data ?? [];
  if (items.length === 0) return <EmptyState variant="card" title="Комментариев нет" icon={Icons.list} />;

  return (
    <div className="space-y-2">
      {items.map((c) => (
        <Card key={c.id}>
          <CardContent className="p-4 space-y-2">
            <div className="flex flex-wrap items-center gap-2 text-xs text-muted-foreground">
              <Badge variant="outline" className="text-xs">
                {c.entityType}
              </Badge>
              <code>{c.entityId}</code>
              <span>·</span>
              <span>{formatDate(c.createdAt)}</span>
              {c.deletedAt ? (
                <Badge variant="destructive" className="text-xs">
                  удалён
                </Badge>
              ) : null}
            </div>
            <p className="text-sm whitespace-pre-wrap">{c.bodyPreview}</p>
          </CardContent>
        </Card>
      ))}
    </div>
  );
}

function Skeletons() {
  return (
    <div className="space-y-2">
      {[1, 2, 3].map((i) => (
        <Skeleton key={i} className="h-20 w-full" />
      ))}
    </div>
  );
}
