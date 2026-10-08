"use client";

import { mockInterviewsQueryOptions } from "@/entities/mock-interview";
import { trainerLimitsQueryOptions } from "@/entities/trainer-limits";
import { trainerMockTrendQueryOptions } from "@/entities/trainer-stats";
import { MockHistory } from "@/features/trainer-mock-history";
import { MockInterviewSetup } from "@/features/start-mock-session";
import { MockTrend } from "@/features/trainer-statistics";
import { getErrorMessage } from "@/shared/api";
import { Icons } from "@/shared/ui/icons";
import { Button } from "@/shared/ui/kit/button";
import { EmptyState } from "@/shared/ui/kit/empty-state";
import { Skeleton } from "@/shared/ui/kit/skeleton";
import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { TrainerProPaywallDialog } from "./trainer-pro-paywall-dialog";

interface HubMockTabProps {
  isAuthenticated: boolean;
}

/**
 * Вкладка «Симуляция» хаба (#568): конфигуратор симуляции по выбранному мок-собеседованию
 * (POSITION-подборка). Аноним просматривает список симуляций read-only (#614 F) — «Начать»
 * ведёт на логин (гейт внутри `MockInterviewSetup`); история (`MockHistory`) — личная, скрыта
 * для анонима. Сам прогон MOCK живёт на `/trainer/session/{id}` (тот же раннер, что DRILL).
 * Мок-собесы — кросс-тематические, трек не выбирается.
 */
export function HubMockTab({ isAuthenticated }: HubMockTabProps) {
  const interviewsQuery = useQuery(mockInterviewsQueryOptions.listOptions());
  // Полная статистика по симуляциям прямо на странице (#568): динамика баллов мок-собесов.
  const mockTrendQuery = useQuery({ ...trainerMockTrendQueryOptions(), enabled: isAuthenticated });
  const mockTrend = mockTrendQuery.data;
  // Статус Pro (#658): мок — PRO-only. Не-подписчику «Начать» открывает пейволл, а не
  // мутацию-403. `isPro` авторитетен (ловит и авто-PRO за полный доступ к платформе).
  const limitsQuery = useQuery({ ...trainerLimitsQueryOptions(), enabled: isAuthenticated });
  const [paywallOpen, setPaywallOpen] = useState(false);

  if (interviewsQuery.isPending) {
    return (
      <div className="mx-auto w-full max-w-2xl space-y-6">
        <Skeleton className="h-24 w-full rounded-xl" />
        <Skeleton className="h-60 w-full rounded-xl" />
      </div>
    );
  }

  if (interviewsQuery.isError) {
    return (
      <EmptyState
        icon={Icons.error}
        variant="card"
        title="Не удалось загрузить мок-собеседования"
        description={getErrorMessage(interviewsQuery.error, "Попробуйте обновить страницу")}
        action={
          <Button variant="outline" onClick={() => interviewsQuery.refetch()}>
            <Icons.refresh className="size-4" />
            Попробовать снова
          </Button>
        }
      />
    );
  }

  const interviews = interviewsQuery.data;

  return (
    <div className="space-y-8">
      {interviews.length === 0 ? (
        <EmptyState
          icon={Icons.briefcase}
          variant="card"
          title="Скоро появятся мок-собесы"
          description="Подборки вопросов под собеседование готовятся — загляни позже."
        />
      ) : (
        <MockInterviewSetup
          interviews={interviews}
          isAuthenticated={isAuthenticated}
          hasPro={limitsQuery.data?.isPro}
          onProRequired={() => setPaywallOpen(true)}
        />
      )}
      <TrainerProPaywallDialog open={paywallOpen} onOpenChange={setPaywallOpen} />
      {/* Полная стата по симуляциям (динамика баллов) + сильные/слабые + история —
          личное, только для залогиненного (#614 F). График показываем при наличии моков. */}
      {isAuthenticated && mockTrend && mockTrend.attempts.length > 0 && (
        <MockTrend trend={mockTrend} />
      )}
      {isAuthenticated && <MockHistory />}
    </div>
  );
}
