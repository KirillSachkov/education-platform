"use client";

import type { AdminPostPurchaseOnboarding } from "@/entities/admin-cross-service";
import { ProgressBar } from "@/shared/ui/components";
import { Badge } from "@/shared/ui/kit/badge";

type OnboardingChecklistProps = {
  onboarding: AdminPostPurchaseOnboarding | null;
};

/**
 * Onboarding-прогресс гранта (#444): X/Y шагов, текущий шаг и chip'ы по pending
 * Telegram / GitHub. Если flow у плана выключен — короткая строка вместо прогресса.
 */
export function OnboardingChecklist({ onboarding }: OnboardingChecklistProps) {
  if (!onboarding || !onboarding.flowEnabled) {
    return <p className="text-xs text-muted-foreground">Онбординг для плана не настроен</p>;
  }

  const { totalSteps, completedSteps, skippedSteps, completed, started } = onboarding;
  const done = completedSteps + skippedSteps;
  const percent = totalSteps > 0 ? Math.round((done / totalSteps) * 100) : 0;

  return (
    <div className="space-y-2">
      <div className="flex flex-wrap items-center gap-2 text-xs">
        <span className="font-medium text-foreground">
          Онбординг: {done} из {totalSteps}
        </span>
        {completed ? (
          <Badge variant="secondary" className="text-[11px]">
            Завершён
          </Badge>
        ) : started ? (
          <Badge variant="outline" className="text-[11px]">
            В процессе
          </Badge>
        ) : (
          <Badge variant="outline" className="text-[11px]">
            Не начат
          </Badge>
        )}
        {skippedSteps > 0 ? (
          <span className="text-muted-foreground">пропущено: {skippedSteps}</span>
        ) : null}
      </div>

      {totalSteps > 0 ? <ProgressBar value={percent} className="h-1.5" /> : null}

      <div className="flex flex-wrap items-center gap-1.5">
        {onboarding.currentStepType ? (
          <Badge variant="outline" className="text-[11px]">
            Текущий шаг: {onboarding.currentStepType}
          </Badge>
        ) : null}
        {onboarding.telegramStepPending ? (
          <Badge variant="outline" className="text-[11px] text-amber-600 dark:text-amber-500">
            Telegram не пройден
          </Badge>
        ) : null}
        {onboarding.githubStepPending ? (
          <Badge variant="outline" className="text-[11px] text-amber-600 dark:text-amber-500">
            GitHub не пройден
          </Badge>
        ) : null}
      </div>
    </div>
  );
}
