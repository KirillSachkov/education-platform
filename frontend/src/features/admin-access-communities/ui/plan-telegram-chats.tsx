"use client";

import { useQuery } from "@tanstack/react-query";
import { adminCrossServiceQueryOptions } from "@/entities/admin-cross-service";
import { Badge } from "@/shared/ui/kit/badge";
import { Skeleton } from "@/shared/ui/kit/skeleton";
import { CopyInviteButton } from "./copy-invite-button";

type PlanTelegramChatsProps = {
  planId: string;
};

/**
 * Telegram-чаты плана (#444) — own query per planId (каждый grant-блок монтирует
 * собственный инстанс, hooks-правила соблюдены). Для каждого чата — заголовок,
 * тип, флаг «вступление даёт членство» и кнопка копирования invite-ссылки.
 */
export function PlanTelegramChats({ planId }: PlanTelegramChatsProps) {
  const query = useQuery(adminCrossServiceQueryOptions.getPlanTelegramChatsOptions(planId));

  if (query.isLoading) return <Skeleton className="h-8 w-full" />;
  if (query.isError) {
    return <p className="text-xs text-muted-foreground">Не удалось загрузить Telegram-чаты</p>;
  }

  const chats = query.data ?? [];
  if (chats.length === 0) {
    return <p className="text-xs text-muted-foreground">К плану не привязаны Telegram-чаты</p>;
  }

  return (
    <ul className="space-y-2">
      {chats.map((chat) => (
        <li
          key={chat.telegramChatId}
          className="flex flex-wrap items-center justify-between gap-2 rounded-md border border-border/50 px-3 py-2"
        >
          <div className="flex min-w-0 flex-wrap items-center gap-x-2 gap-y-1">
            <span className="min-w-0 truncate text-sm font-medium">
              {chat.chatTitle ?? <code className="text-xs">{chat.telegramChatId}</code>}
            </span>
            <Badge variant="outline" className="text-[11px]">
              {chat.chatType}
            </Badge>
            {chat.enrollmentGrantsMembership ? (
              <Badge variant="secondary" className="text-[11px]">
                Вступление = членство
              </Badge>
            ) : null}
          </div>
          {chat.inviteLink ? (
            <CopyInviteButton inviteLink={chat.inviteLink} label="Скопировать ссылку на чат" />
          ) : (
            <span className="text-xs text-muted-foreground">нет invite-ссылки</span>
          )}
        </li>
      ))}
    </ul>
  );
}
