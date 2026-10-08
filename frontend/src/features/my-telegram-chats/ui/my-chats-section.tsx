"use client";

import { planTelegramChatQueryOptions } from "@/entities/plan-telegram-chat";
import type { MyProfile } from "@/entities/profile";
import { getErrorMessage } from "@/shared/api";
import { Icons } from "@/shared/ui/icons";
import { Button } from "@/shared/ui/kit/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/shared/ui/kit/card";
import { Skeleton } from "@/shared/ui/kit/skeleton";
import { useQuery } from "@tanstack/react-query";

interface Props {
  profile: MyProfile;
}

/**
 * Список Telegram-чатов планов, на которые у юзера есть активный grant.
 * Использует /telegram/me/chats. Если TG не привязан — показываем CTA «Привяжи TG».
 * Если привязан, но чатов нет — empty state.
 *
 * Названия планов приходят с бэка index-aligned с planIds (chat.planTitles) — фронт
 * не делает дополнительных запросов к /access/plans/{id}.
 */
export function MyTelegramChatsSection({ profile }: Props) {
  const enabled = profile.hasTelegramLinked;

  const {
    data: chats,
    isLoading,
    error,
  } = useQuery({
    ...planTelegramChatQueryOptions.myChats(),
    enabled,
  });

  const chatList = chats ?? [];

  if (!enabled) {
    return (
      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2 text-base">
            <Icons.telegram className="size-4" />
            Чаты планов в Telegram
          </CardTitle>
          <CardDescription>
            Привяжи Telegram, чтобы получать invite-ссылки в чаты по планам автоматически.
          </CardDescription>
        </CardHeader>
      </Card>
    );
  }

  if (isLoading) {
    return (
      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2 text-base">
            <Icons.telegram className="size-4" />
            Чаты планов в Telegram
          </CardTitle>
        </CardHeader>
        <CardContent className="space-y-2">
          <Skeleton className="h-16 w-full" />
          <Skeleton className="h-16 w-full" />
        </CardContent>
      </Card>
    );
  }

  if (error) {
    return (
      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2 text-base">
            <Icons.telegram className="size-4" />
            Чаты планов в Telegram
          </CardTitle>
        </CardHeader>
        <CardContent>
          <p className="text-sm text-destructive">
            {getErrorMessage(error, "Не удалось загрузить список чатов")}
          </p>
        </CardContent>
      </Card>
    );
  }

  if (chatList.length === 0) {
    return (
      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2 text-base">
            <Icons.telegram className="size-4" />
            Чаты планов в Telegram
          </CardTitle>
          <CardDescription>
            По вашим активным планам пока не привязано ни одного Telegram-чата.
          </CardDescription>
        </CardHeader>
      </Card>
    );
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-base">
          <Icons.telegram className="size-4" />
          Чаты планов в Telegram
        </CardTitle>
        <CardDescription>
          Telegram-чаты, в которые ты можешь вступить — бот пустит автоматически.
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-2">
        {chatList.map((chat) => {
          const titles = chat.planTitles.filter((t) => t.length > 0);
          const plansLabel = titles.length > 0 ? titles.join(" • ") : null;
          return (
            <div
              key={chat.telegramChatId}
              className="flex items-start justify-between gap-3 rounded-md border p-3"
            >
              <div className="min-w-0 space-y-1">
                <p className="font-medium text-sm">{chat.chatTitle ?? "Без названия"}</p>
                <p className="text-xs text-muted-foreground">
                  {chat.chatType === "CHANNEL" ? "Канал" : "Группа"}
                  {plansLabel ? ` • ${plansLabel}` : null}
                </p>
              </div>
              <Button asChild size="sm" variant={chat.isMember ? "ghost" : "outline"}>
                <a href={chat.inviteLink} target="_blank" rel="noopener noreferrer">
                  <Icons.externalLink className="mr-1 size-3.5" />
                  {chat.isMember ? "Открыть" : "Войти"}
                </a>
              </Button>
            </div>
          );
        })}
      </CardContent>
    </Card>
  );
}
