"use client";

import type { CurrentOnboardingResponse, OnboardingStepDto } from "@/entities/plan-onboarding";
import { Button } from "@/shared/ui/kit/button";
import { Icons } from "@/shared/ui/icons";
import { useEffect } from "react";
import { deriveWizardView } from "../model/wizard-state";
import {
  useCompleteOnboarding,
  useCompleteStep,
  useReturnToStep,
  useSkipStep,
} from "../model/use-step-actions";
import { CompletionView } from "./completion-view";
import { GithubReviewAppStepView } from "./step-views/github-review-app-step-view";
import { GithubStepView } from "./step-views/github-step-view";
import { MarkdownStepView } from "./step-views/markdown-step-view";
import { NotificationsStepView } from "./step-views/notifications-step-view";
import { TelegramStepView } from "./step-views/telegram-step-view";

type Props = {
  onboarding: CurrentOnboardingResponse;
  /**
   * Хуковая «инъекция» Telegram-привязки. Композируется на странице
   * (`useTelegramLink` из `features/telegram-link`), чтобы `onboarding-wizard`
   * не зависел напрямую от `features/telegram-link`. Применяется в TELEGRAM-шаге.
   */
  onLinkTelegram: () => void;
  isLinkingTelegram: boolean;
  /**
   * Свернуть онбординг («Позже»). Шаги остаются обязательными, но пользователь
   * не заперт в модалке, если шаг не проходит (например, привязка Telegram). См. #612.
   */
  onDismiss: () => void;
};

/**
 *  Wizard рендерится как контент modal-диалога (`widgets/onboarding-overlay`).
 *  Layout — три блока в столбик: header → scrollable main → footer.
 *  Шаг выбирается по `state.currentStepId` (server-side single source of truth):
 *  после skip/complete мутация invalidates query, query refetch'ит свежий
 *  `onboarding` с обновлённым currentStepId, wizard перерисовывается.
 *  Когда currentStepId == null — CompletionView.
 */
export function OnboardingWizard({
  onboarding,
  onLinkTelegram,
  isLinkingTelegram,
  onDismiss,
}: Props) {
  const skip = useSkipStep(onboarding.planId);
  const complete = useCompleteStep(onboarding.planId);
  const returnTo = useReturnToStep(onboarding.planId);
  const finish = useCompleteOnboarding(onboarding.planId);

  const view = deriveWizardView(onboarding.steps, onboarding.state);
  const currentStepId = view.kind === "completion" ? null : view.step.id;

  // Сброс sticky-ошибки «Далее» при смене шага: react-query держит mutation.error
  // до следующего mutate/reset. Иначе при возврате на TELEGRAM-шаг (Назад → Вперёд)
  // прошлая 400 membership.required снова покажет inline-алерт как ghost-state,
  // хотя кнопку никто не нажимал. `reset` у react-query стабилен между рендерами.
  const { reset: resetComplete } = complete;
  useEffect(() => {
    resetComplete();
  }, [currentStepId, resetComplete]);

  if (view.kind === "completion") {
    return (
      <CompletionView
        planTitle={onboarding.planDisplayName}
        onFinish={() => finish.mutate()}
        isFinishing={finish.isPending}
      />
    );
  }

  const { step: currentStep, index: effectiveIndex, total: totalSteps } = view;

  const currentInFlowIdx = onboarding.steps.findIndex((s) => s.id === currentStep.id);
  const prevStepId = currentInFlowIdx > 0 ? onboarding.steps[currentInFlowIdx - 1].id : null;

  const handleSkip = () => skip.mutate(currentStep.id);
  const handleComplete = () => complete.mutate(currentStep.id);
  const handleBack = () => {
    if (prevStepId) returnTo.mutate(prevStepId);
  };

  return (
    <div className="flex max-h-[90dvh] flex-col">
      {/* header/footer — shrink-0, чтобы на длинном шаге (GITHUB_REVIEW_APP на
          мобиле) скроллился только main, а заголовок/кнопки оставались
          закреплёнными и не уезжали за край (#497 дизайн-ревью). */}
      <header className="shrink-0 space-y-3 border-b px-6 py-4">
        <div className="flex items-center justify-between gap-3">
          <h2 className="truncate font-semibold">{onboarding.planDisplayName}</h2>
          <div className="flex shrink-0 items-center gap-2">
            <span className="text-sm text-muted-foreground">
              Шаг {effectiveIndex + 1} из {totalSteps}
            </span>
            <Button
              variant="ghost"
              size="sm"
              className="h-auto px-2 py-1 text-xs text-muted-foreground"
              onClick={onDismiss}
              title="Свернуть — можно вернуться к настройке позже"
            >
              Позже
            </Button>
          </div>
        </div>
        <Stepper
          steps={onboarding.steps}
          currentStepId={currentStep.id}
          completedStepIds={onboarding.state.completedStepIds}
          skippedStepIds={onboarding.state.skippedStepIds}
          isPending={returnTo.isPending}
          onJump={(stepId) => returnTo.mutate(stepId)}
        />
      </header>

      <main className="flex-1 overflow-y-auto px-6 py-6">
        <StepRenderer
          step={currentStep}
          planId={onboarding.planId}
          planGitHubOrgSlug={onboarding.planGitHubOrgSlug}
          onLinkTelegram={onLinkTelegram}
          isLinkingTelegram={isLinkingTelegram}
          completeError={complete.error}
        />
      </main>

      <footer className="flex shrink-0 items-center justify-between gap-2 border-t px-6 py-4">
        <Button variant="ghost" disabled={!prevStepId || returnTo.isPending} onClick={handleBack}>
          ← Назад
        </Button>
        <div className="flex items-center gap-2">
          {currentStep.isSkippable && (
            <Button variant="ghost" disabled={skip.isPending} onClick={handleSkip}>
              Пропустить
            </Button>
          )}
          <Button disabled={complete.isPending} onClick={handleComplete}>
            {complete.isPending && <Icons.loading className="h-4 w-4 animate-spin" />}
            Далее
          </Button>
        </div>
      </footer>
    </div>
  );
}

type StepperProps = {
  steps: ReadonlyArray<OnboardingStepDto>;
  currentStepId: string;
  completedStepIds: ReadonlyArray<string>;
  skippedStepIds: ReadonlyArray<string>;
  isPending: boolean;
  onJump: (stepId: string) => void;
};

/**
 *  Линейка точек-шагов с цветовой индикацией прогресса. Клик по точке отправляет
 *  POST /return-to/ — wizard переключается на этот шаг (можно листать туда-сюда).
 */
function Stepper({
  steps,
  currentStepId,
  completedStepIds,
  skippedStepIds,
  isPending,
  onJump,
}: StepperProps) {
  return (
    <div className="flex items-center gap-1">
      {steps.map((step) => {
        const isCurrent = step.id === currentStepId;
        const isCompleted = completedStepIds.includes(step.id);
        const isSkipped = skippedStepIds.includes(step.id);
        const tone = isCurrent
          ? "bg-primary"
          : isCompleted
            ? "bg-emerald-500"
            : isSkipped
              ? "bg-muted-foreground/40"
              : "bg-muted";
        return (
          <button
            key={step.id}
            type="button"
            disabled={isPending || isCurrent}
            onClick={() => onJump(step.id)}
            title={
              isCurrent
                ? "Текущий шаг"
                : isCompleted
                  ? "Пройден — нажми, чтобы вернуться"
                  : isSkipped
                    ? "Пропущен — нажми, чтобы вернуться"
                    : "Не пройден"
            }
            className={`h-1.5 flex-1 rounded-full transition-all ${tone} ${
              !isCurrent && !isPending ? "cursor-pointer hover:opacity-70" : ""
            }`}
            aria-label={`Шаг ${step.stepType}`}
          />
        );
      })}
    </div>
  );
}

function StepRenderer({
  step,
  planId,
  planGitHubOrgSlug,
  onLinkTelegram,
  isLinkingTelegram,
  completeError,
}: {
  step: OnboardingStepDto;
  planId: string;
  planGitHubOrgSlug: string | null;
  onLinkTelegram: () => void;
  isLinkingTelegram: boolean;
  completeError?: unknown;
}) {
  switch (step.stepType) {
    case "MARKDOWN":
      return <MarkdownStepView title={step.title ?? ""} body={step.body ?? ""} />;
    case "TELEGRAM":
      return (
        <TelegramStepView
          planId={planId}
          onLinkTelegram={onLinkTelegram}
          isLinking={isLinkingTelegram}
          completeError={completeError}
        />
      );
    case "GITHUB":
      return (
        <GithubStepView
          planId={planId}
          planGitHubOrgSlug={planGitHubOrgSlug}
          completeError={completeError}
        />
      );
    case "GITHUB_REVIEW_APP":
      return <GithubReviewAppStepView />;
    case "NOTIFICATIONS":
      return <NotificationsStepView />;
    default:
      return null;
  }
}
