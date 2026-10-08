"use client";

import {
  type CurrentOnboardingResponse,
  currentOnboardingQueryOptions,
} from "@/entities/plan-onboarding";
import { OnboardingWizard } from "@/features/onboarding-wizard";
import { useTelegramLink } from "@/features/telegram-link";
import { useIsAuthenticated } from "@/shared/auth/use-is-authenticated";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@/shared/ui/kit/dialog";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useSession } from "next-auth/react";
import { useState } from "react";

const DISMISS_KEY_PREFIX = "onboarding-dismissed:";

// Сворачивание онбординга запоминается на сессию (sessionStorage): в рамках текущей
// вкладки оверлей больше не всплывает, но после нового входа/сессии снова напомнит
// завершить настройку. Так пользователь не оказывается заперт, если шаг (например
// привязка Telegram) почему-то не проходит, но и не «теряет» онбординг навсегда.
function readDismissed(planId: string): boolean {
  try {
    return sessionStorage.getItem(DISMISS_KEY_PREFIX + planId) === "1";
  } catch {
    return false;
  }
}

function rememberDismissed(planId: string): void {
  try {
    sessionStorage.setItem(DISMISS_KEY_PREFIX + planId, "1");
  } catch {
    // sessionStorage недоступен (private mode и т.п.) — не критично: оверлей просто
    // покажется снова на следующем рендере.
  }
}

/**
 *  Plan-onboarding как modal overlay поверх текущей страницы. Mount'ится в
 *  `(app)/layout.tsx` и сидит идлом пока у юзера нет pending onboarding.
 *
 *  Внешний компонент только тащит query — никаких hook'ов с side effects,
 *  чтобы на «холостых» рендерах (99% страниц) не аллоцировать mutation state.
 *  Дочерний `<OnboardingDialog>` появляется только когда есть `data`, и уже
 *  внутри него композируется `useTelegramLink` (нужный лишь TELEGRAM-шагу).
 */
export function OnboardingOverlay() {
  const isAuthenticated = useIsAuthenticated();
  const { data: session, status } = useSession();
  const { data } = useQuery({
    ...currentOnboardingQueryOptions,
    enabled: isAuthenticated,
    // Держим предыдущие данные во время refetch'а (каждый skip/complete/return
    // инвалидирует `current-onboarding`) — иначе на момент перезапроса `data`
    // сбрасывается, шаг ремоунтится и контент мерцает.
    placeholderData: (prev) => prev,
  });

  // Профильный гейт «Как вас называть?» активен — план-онбординг ждёт своей
  // очереди (#497): два стэкнутых модальных окна путали новых юзеров (скрин
  // Константина). Условие зеркалит RequireDisplayNameGate (соседний mount в
  // (app)/layout.tsx): displayName === "" ⇒ имя ещё не задано.
  const displayNameGateActive =
    status === "authenticated" && session?.user.displayName === "";

  if (!data || displayNameGateActive) return null;

  // key по planId — при смене плана компонент перемонтируется и заново читает
  // состояние «свёрнуто» для нового плана.
  return <OnboardingDialog key={data.planId} onboarding={data} />;
}

function OnboardingDialog({ onboarding }: { onboarding: CurrentOnboardingResponse }) {
  const queryClient = useQueryClient();
  const [dismissed, setDismissed] = useState(() => readDismissed(onboarding.planId));
  const { linkTelegram, isPending: isLinkingTelegram } = useTelegramLink({
    onLinked: () => {
      queryClient.invalidateQueries({
        queryKey: ["plan-onboarding", "telegram-status", onboarding.planId],
      });
    },
  });

  const handleDismiss = () => {
    rememberDismissed(onboarding.planId);
    setDismissed(true);
  };

  return (
    <Dialog open={!dismissed}>
      <DialogContent
        showCloseButton={false}
        className="max-h-[90dvh] gap-0 overflow-hidden p-0 sm:max-w-2xl"
        onEscapeKeyDown={(e) => e.preventDefault()}
        onPointerDownOutside={(e) => e.preventDefault()}
        onInteractOutside={(e) => e.preventDefault()}
      >
        {/* Радикс требует DialogTitle для screen-reader'ов; визуальный заголовок
            рисует сам wizard в своём header'е, поэтому здесь — sr-only. */}
        <DialogHeader className="sr-only">
          <DialogTitle>Онбординг плана «{onboarding.planDisplayName}»</DialogTitle>
          <DialogDescription>
            Пошаговая настройка доступа: Telegram, GitHub, уведомления.
          </DialogDescription>
        </DialogHeader>
        <OnboardingWizard
          onboarding={onboarding}
          onLinkTelegram={() => linkTelegram()}
          isLinkingTelegram={isLinkingTelegram}
          onDismiss={handleDismiss}
        />
      </DialogContent>
    </Dialog>
  );
}
