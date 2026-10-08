"use client";

import { useQuery } from "@tanstack/react-query";
import { adminCrossServiceQueryOptions } from "@/entities/admin-cross-service";
import { Badge } from "@/shared/ui/kit/badge";
import { Card, CardContent } from "@/shared/ui/kit/card";
import { Icons } from "@/shared/ui/icons";
import { Skeleton } from "@/shared/ui/kit/skeleton";
import { formatDate } from "../lib/format";

type TelegramIdentityBlockProps = {
  userId: string;
};

/**
 * Telegram-привязка юзера сверху вкладки (#444): привязан ли аккаунт, @username,
 * дата привязки. Отдельный query (TBS), независим от post-purchase-status.
 */
export function TelegramIdentityBlock({ userId }: TelegramIdentityBlockProps) {
  const query = useQuery(adminCrossServiceQueryOptions.getUserTelegramLinkOptions(userId));

  return (
    <Card>
      <CardContent className="flex flex-wrap items-center gap-x-4 gap-y-2 p-4">
        <div className="flex items-center gap-2">
          <span className="flex size-7 shrink-0 items-center justify-center rounded-lg bg-sky-500/10 text-sky-600 dark:text-sky-400">
            <Icons.telegram className="size-3.5" />
          </span>
          <span className="text-sm font-medium">Telegram</span>
        </div>

        {query.isLoading ? (
          <Skeleton className="h-5 w-40" />
        ) : query.isError || !query.data ? (
          <span className="text-xs text-muted-foreground">Не удалось загрузить</span>
        ) : query.data.linked ? (
          <div className="flex flex-wrap items-center gap-x-3 gap-y-1 text-sm">
            <Badge variant="secondary" className="text-[11px]">
              Привязан
            </Badge>
            {query.data.telegramUsername ? (
              <span className="text-foreground">@{query.data.telegramUsername}</span>
            ) : query.data.telegramUserId != null ? (
              <code className="text-xs text-muted-foreground">id {query.data.telegramUserId}</code>
            ) : null}
            <span className="text-xs text-muted-foreground">
              с {formatDate(query.data.linkedAt)}
            </span>
          </div>
        ) : (
          <Badge variant="outline" className="text-[11px]">
            Не привязан
          </Badge>
        )}
      </CardContent>
    </Card>
  );
}
