"use client";

import { telegramOnboardingStatusQueryOptions } from "@/entities/plan-onboarding";
import { profileQueryOptions } from "@/entities/profile";
import { isEnvelopeError } from "@/shared/api";
import { PRIMARY_AUTHOR_CONSULTATION_LINK } from "@/shared/config/primary-author";
import { Icons } from "@/shared/ui/icons";
import { Button } from "@/shared/ui/kit/button";
import { Card } from "@/shared/ui/kit/card";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect } from "react";
import { useRecheckTelegramMembership } from "../../model/use-step-actions";

const TELEGRAM_MEMBERSHIP_REQUIRED_CODE = "onboarding.telegram.membership.required";

type Props = {
  planId: string;
  /**
   * Колбэк привязки Telegram. Композируется на странице (через
   * `features/telegram-link.useTelegramLink`), чтобы `features/onboarding-wizard`
   * не зависел напрямую от `features/telegram-link`.
   */
  onLinkTelegram: () => void;
  isLinking: boolean;
  /**
   * Ошибка последней попытки «Далее» (footer wizard'а → completeStep). Прокидывается
   * сверху, чтобы TELEGRAM-шаг показал inline-обратную связь, когда mandatory-проверка
   * членства отклонила завершение. Без этого единственным каналом ошибки оставался
   * sonner-тост, который в полноэкранной modal-онбординга на мобиле перекрывается —
   * и «Далее» казалось «ничего не делает». Теперь «Далее» и «Я вступил — проверить»
   * дают один и тот же inline-статус (не расходятся).
   */
  completeError?: unknown;
};

/** supergroup/group/channel/private → человекочитаемый label (а не сырой type из Telegram). */
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
 *  Telegram-step. Подтягивает /telegram/me/onboarding-status — статус привязки
 *  и список чатов плана. Если tg не привязан — inline-кнопка, которая дергает
 *  `onLinkTelegram` (page-level hook поллит профиль до hasTelegramLinked=true).
 */
export function TelegramStepView({ planId, onLinkTelegram, isLinking, completeError }: Props) {
  const queryClient = useQueryClient();
  // Profile invalidate при mount — юзер мог привязать TG в settings или другом
  // tab'е, кэш устаревает; синхронизируем чтобы wizard не отставал.
  useEffect(() => {
    queryClient.invalidateQueries({ queryKey: profileQueryOptions.getMyProfileKey() });
  }, [queryClient]);

  const { data, isPending } = useQuery(telegramOnboardingStatusQueryOptions(planId));
  const recheck = useRecheckTelegramMembership(planId);

  if (isPending || !data) {
    return <div className="text-muted-foreground">Загрузка…</div>;
  }

  // «Членство не подтверждено» из любого источника: отклонённое «Далее»
  // (server-side mandatory-проверка вернула 400) ИЛИ recheck, вернувший
  // not_member/unknown. Оба ведут к одному inline-статусу — кнопки не расходятся.
  // (Сам алерт рендерится ниже, в ветке data.isLinked.)
  const completeRejectedForMembership =
    isEnvelopeError(completeError) &&
    completeError.messages.some((m) => m.code === TELEGRAM_MEMBERSHIP_REQUIRED_CODE);
  const recheckResult = recheck.isSuccess ? recheck.data.result : null;
  const recheckNotConfirmed = recheckResult?.completed === false;
  const notConfirmed = completeRejectedForMembership || recheckNotConfirmed;
  // «unknown» (мягкая формулировка «не удалось проверить») — только когда recheck
  // ЯВНО вернул unknown. На «Далее» бэк схлопывает not_member/unknown/сервис-недоступен
  // в один 400, sub-статус неизвестен → показываем безопасный дефолт «вступи и проверь».
  const statusUnknown = recheckResult?.status === "unknown";

  return (
    <div className="@container space-y-4">
      <div className="flex items-center gap-3">
        <Icons.telegram className="h-8 w-8 text-primary" />
        <h2 className="text-2xl font-semibold">Подключи Telegram</h2>
      </div>

      {!data.isLinked ? (
        <>
          <p className="text-muted-foreground">
            Привяжи Telegram-аккаунт. Бот откроется в новой вкладке — нажми Start, и страница
            автоматически обновится.
          </p>
          <Button onClick={() => onLinkTelegram()} disabled={isLinking}>
            <Icons.telegram className="h-4 w-4" />
            {isLinking ? "Открываем бота…" : "Привязать Telegram"}
          </Button>
        </>
      ) : (
        <>
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
              ? "Бот отправит тебе invite-link'и в личку. Также можно зайти вручную:"
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
                        <Button
                          asChild
                          size="sm"
                          variant="outline"
                          className="w-full shrink-0 @sm:w-auto"
                        >
                          <a href={chat.joinUrl} target="_blank" rel="noopener noreferrer">
                            <Icons.externalLink className="h-4 w-4" />
                            Открыть чат
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
                  Уже вступил в чат? Нажми — мы проверим членство, и шаг отметится автоматически.
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

          {notConfirmed && (
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
        </>
      )}
    </div>
  );
}
