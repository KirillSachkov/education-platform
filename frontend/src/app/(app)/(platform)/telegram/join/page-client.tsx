"use client";

import { telegramOnboardingStatusQueryOptions } from "@/entities/plan-onboarding";
import { TelegramJoinView } from "@/features/telegram-join";
import { useTelegramLink } from "@/features/telegram-link";
import { useQueryClient } from "@tanstack/react-query";

/**
 * Client-обвязка `/telegram/join`. Композирует `useTelegramLink` (привязка +
 * polling профиля) на уровне страницы — `features/telegram-join` остаётся
 * чистым от cross-slice зависимости на `features/telegram-link` (FSD).
 *
 * `onLinked` после фиксации привязки инвалидирует telegram-status плана, чтобы
 * карточка сразу перешла из «Привязать Telegram» в список чатов без рефреша.
 */
export function TelegramJoinPageClient({ planId }: { planId: string | null }) {
  const queryClient = useQueryClient();
  const { linkTelegram, isPending } = useTelegramLink({
    onLinked: () => {
      if (planId) {
        void queryClient.invalidateQueries({
          queryKey: telegramOnboardingStatusQueryOptions(planId).queryKey,
        });
      }
    },
  });

  return (
    <TelegramJoinView
      planId={planId}
      onLinkTelegram={() => linkTelegram()}
      isLinking={isPending}
    />
  );
}
