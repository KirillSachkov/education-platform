"use client";

import { telegramOnboardingStatusQueryOptions } from "@/entities/plan-onboarding";
import { profileQueryOptions } from "@/entities/profile";
import { PRIMARY_AUTHOR_CONSULTATION_LINK } from "@/shared/config/primary-author";
import { routes } from "@/shared/config/routes";
import { Icons } from "@/shared/ui/icons";
import { Button } from "@/shared/ui/kit/button";
import { Card } from "@/shared/ui/kit/card";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { useEffect } from "react";
import { useRecheckMembership } from "../model/use-recheck-membership";

type Props = {
  /** `?plan=` из URL. `null` — пользователь пришёл без контекста плана. */
  planId: string | null;
  /**
   * Колбэк привязки Telegram. Композируется на странице (через
   * `features/telegram-link.useTelegramLink`), чтобы `features/telegram-join`
   * не зависел напрямую от другого feature-слайса (FSD: no cross-slice imports).
   */
  onLinkTelegram: () => void;
  isLinking: boolean;
};

/** supergroup/group/channel/private → человекочитаемый label. */
function chatTypeLabel(chatType: string): string {
  switch (chatType.toLowerCase()) {
    case "channel":
      return "Канал";
    case "group":
    case "supergroup":
      return "Группа";
    case "private":
      return "Личный чат";
    default:
      return "Чат";
  }
}

/**
 * Standalone-страница «Вступи в Telegram-группу» (`/telegram/join?plan=...`).
 *
 * Воспроизводит логику TELEGRAM-шага онбординга, но работает автономно — даже
 * если онбординг был пропущен или завершён. Точка приземления для
 * out-of-band уведомлений/писем (`TelegramJoinReminder`), CTA которых должен
 * провести юзера через (1) привязку Telegram и (2) вступление в группу: бот
 * отклоняет join-request'ы от непривязанных аккаунтов.
 */
export function TelegramJoinView({ planId, onLinkTelegram, isLinking }: Props) {
  if (!planId) {
    return <NoPlanView onLinkTelegram={onLinkTelegram} isLinking={isLinking} />;
  }
  return <PlanJoinView planId={planId} onLinkTelegram={onLinkTelegram} isLinking={isLinking} />;
}

/** Нет `?plan=` — graceful generic: привязать Telegram + уйти к своим планам. */
function NoPlanView({ onLinkTelegram, isLinking }: Pick<Props, "onLinkTelegram" | "isLinking">) {
  return (
    <Shell>
      <p className="text-muted-foreground">
        Чтобы попасть в Telegram-группу курса, привяжи Telegram-аккаунт. Бот добавит тебя в группы
        по купленным планам автоматически.
      </p>
      <div className="flex flex-col gap-2 @sm:flex-row">
        <Button onClick={() => onLinkTelegram()} disabled={isLinking}>
          <Icons.telegram className="h-4 w-4" />
          {isLinking ? "Открываем бота…" : "Привязать Telegram"}
        </Button>
        <Button asChild variant="outline">
          <Link href={routes.myPlans}>
            <Icons.arrowRight className="h-4 w-4" />
            Перейти к моим планам
          </Link>
        </Button>
      </div>
    </Shell>
  );
}

function PlanJoinView({ planId, onLinkTelegram, isLinking }: { planId: string } & Pick<Props, "onLinkTelegram" | "isLinking">) {
  const queryClient = useQueryClient();
  // Юзер мог привязать TG в settings/другом табе — синхронизируем профиль,
  // чтобы страница не отставала от реального состояния привязки.
  useEffect(() => {
    queryClient.invalidateQueries({ queryKey: profileQueryOptions.getMyProfileKey() });
  }, [queryClient]);

  const { data, isPending } = useQuery(telegramOnboardingStatusQueryOptions(planId));
  const recheck = useRecheckMembership(planId);

  if (isPending || !data) {
    return (
      <Shell>
        <div className="text-muted-foreground">Загрузка…</div>
      </Shell>
    );
  }

  const recheckResult = recheck.isSuccess ? recheck.data.result : null;
  // Подтверждённое членство — терминальное состояние: «вы уже в группе».
  const isMember = recheckResult?.completed === true;
  // Recheck вернул not_member/unknown → показываем hint «зайди и проверь снова».
  const recheckNotConfirmed = recheckResult?.completed === false;
  const statusUnknown = recheckResult?.status === "unknown";

  if (!data.isLinked) {
    return (
      <Shell>
        <p className="text-muted-foreground">
          Сначала привяжи Telegram-аккаунт. Бот откроется в новой вкладке — нажми Start, и страница
          автоматически обновится. После привязки бот добавит тебя в группу.
        </p>
        <Button onClick={() => onLinkTelegram()} disabled={isLinking}>
          <Icons.telegram className="h-4 w-4" />
          {isLinking ? "Открываем бота…" : "Привязать Telegram"}
        </Button>
      </Shell>
    );
  }

  if (isMember) {
    return (
      <Shell>
        <div className="flex gap-2.5 rounded-lg border border-green-500/30 bg-green-500/10 p-3 text-sm">
          <Icons.completed className="mt-0.5 h-4 w-4 shrink-0 text-green-600 dark:text-green-500" />
          <div className="min-w-0 space-y-1">
            <p className="font-medium text-foreground">Вы уже в группе</p>
            <p className="text-muted-foreground">
              Членство подтверждено. Можно возвращаться к обучению.
            </p>
          </div>
        </div>
        <div className="flex flex-col gap-2 @sm:flex-row">
          <Button asChild>
            <Link href={routes.home}>
              <Icons.home className="h-4 w-4" />
              На главную
            </Link>
          </Button>
          <Button asChild variant="outline">
            <Link href={routes.myPlans}>Мои планы</Link>
          </Button>
        </div>
      </Shell>
    );
  }

  return (
    <Shell>
      <p className="text-muted-foreground">
        Telegram привязан{" "}
        {data.telegramUsername ? (
          <>
            как <strong>@{data.telegramUsername}</strong>.
          </>
        ) : (
          "."
        )}{" "}
        {data.chats.length > 0
          ? "Бот отправит invite-link'и в личку. Также можно зайти вручную:"
          : "У этого плана пока нет привязанных чатов."}
      </p>

      {data.chats.length > 0 && (
        <>
          <ul className="space-y-2">
            {data.chats.map((chat) => (
              <li key={chat.chatId}>
                <Card className="flex flex-col gap-3 p-3 @sm:flex-row @sm:items-center @sm:justify-between">
                  <div className="flex min-w-0 items-center gap-3">
                    <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-primary/10 text-primary">
                      <Icons.telegram className="h-4 w-4" />
                    </span>
                    <div className="min-w-0">
                      <div className="truncate font-medium leading-tight">
                        {chat.title ?? "Чат"}
                      </div>
                      <div className="text-xs text-muted-foreground">
                        {chatTypeLabel(chat.chatType)}
                      </div>
                    </div>
                  </div>
                  {chat.joinUrl ? (
                    <Button asChild size="sm" variant="outline" className="w-full shrink-0 @sm:w-auto">
                      <a href={chat.joinUrl} target="_blank" rel="noopener noreferrer">
                        <Icons.externalLink className="h-4 w-4" />
                        Вступить
                      </a>
                    </Button>
                  ) : (
                    <span className="text-xs text-muted-foreground @sm:shrink-0">
                      Приглашение придёт в личку бота
                    </span>
                  )}
                </Card>
              </li>
            ))}
          </ul>

          <div className="space-y-2 rounded-lg border border-border/60 bg-muted/30 p-3">
            <p className="text-sm text-muted-foreground">
              Уже вступил в чат? Нажми — мы проверим членство.
            </p>
            <Button
              variant="outline"
              size="sm"
              className="w-full @sm:w-auto"
              disabled={recheck.isPending}
              onClick={() => recheck.mutate()}
            >
              {recheck.isPending ? (
                <>
                  <Icons.loading className="h-4 w-4 animate-spin" />
                  Проверяем…
                </>
              ) : (
                <>
                  <Icons.refresh className="h-4 w-4" />Я вступил — проверить
                </>
              )}
            </Button>
          </div>
        </>
      )}

      {recheckNotConfirmed && (
        <div
          role="alert"
          className="flex gap-2.5 rounded-lg border border-amber-500/30 bg-amber-500/10 p-3 text-sm"
        >
          <Icons.warning className="mt-0.5 h-4 w-4 shrink-0 text-amber-600 dark:text-amber-500" />
          <div className="min-w-0 space-y-1">
            <p className="font-medium text-foreground">Пока не видим тебя в группе</p>
            <p className="text-muted-foreground">
              {data.chats.length === 0
                ? "У плана нет привязанных чатов для автопроверки. "
                : statusUnknown
                  ? "Не удалось проверить членство. Зайди в чат по ссылке выше и нажми «Я вступил — проверить». "
                  : "Сначала зайди в чат по ссылке выше, потом нажми «Я вступил — проверить». "}
              Если уже вступил, а проверка не проходит — напиши в поддержку.
            </p>
            <a
              href={PRIMARY_AUTHOR_CONSULTATION_LINK}
              target="_blank"
              rel="noopener noreferrer"
              className="inline-flex items-center gap-1 font-medium text-primary hover:underline"
            >
              <Icons.telegram className="h-3.5 w-3.5" />
              Написать в поддержку
            </a>
          </div>
        </div>
      )}
    </Shell>
  );
}

/** Общий каркас страницы: иконка + заголовок + контент. */
function Shell({ children }: { children: React.ReactNode }) {
  return (
    <div className="@container mx-auto max-w-2xl space-y-4 px-4 py-6 sm:py-10">
      <div className="flex items-center gap-3">
        <span className="flex h-11 w-11 shrink-0 items-center justify-center rounded-2xl bg-primary/10 text-primary">
          <Icons.telegram className="h-6 w-6" />
        </span>
        <h1 className="text-2xl font-semibold tracking-tight">Вступи в Telegram-группу</h1>
      </div>
      {children}
    </div>
  );
}
