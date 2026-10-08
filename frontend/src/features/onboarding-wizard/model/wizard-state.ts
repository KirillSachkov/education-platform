import type { OnboardingStepDto, UserOnboardingStateDto } from "@/entities/plan-onboarding";

/**
 *  Pure derivation of wizard view state from server-side onboarding data.
 *  Чистая функция, легко тестируется. Wizard.tsx использует её результат для
 *  рендеринга текущего шага либо CompletionView.
 */
export type WizardView =
  | { kind: "step"; step: OnboardingStepDto; index: number; total: number }
  | { kind: "completion" };

export function deriveWizardView(
  steps: ReadonlyArray<OnboardingStepDto>,
  state: Pick<UserOnboardingStateDto, "currentStepId" | "completedStepIds" | "skippedStepIds">,
): WizardView {
  // #497: CompletionView показываем ТОЛЬКО когда pending-шагов не осталось.
  // Курсор может быть null/висячим при живых pending-шагах (автор добавил или
  // удалил шаг после того, как юзер дошёл до «старого конца») — сервер
  // self-heal'ит курсор в GET current, а это — защита от stale-кэша: иначе
  // юзер видел «Всё готово», а POST /complete/ бил 409 has.pending.steps.
  const firstPendingIndex = steps.findIndex(
    (s) => !state.completedStepIds.includes(s.id) && !state.skippedStepIds.includes(s.id),
  );

  if (!state.currentStepId) {
    if (firstPendingIndex >= 0) {
      return {
        kind: "step",
        step: steps[firstPendingIndex],
        index: firstPendingIndex,
        total: steps.length,
      };
    }
    return { kind: "completion" };
  }

  const index = steps.findIndex((s) => s.id === state.currentStepId);
  if (index < 0) {
    // CurrentStepId указывает на несуществующий шаг (например, удалён автором).
    if (firstPendingIndex >= 0) {
      return {
        kind: "step",
        step: steps[firstPendingIndex],
        index: firstPendingIndex,
        total: steps.length,
      };
    }
    return { kind: "completion" };
  }

  return { kind: "step", step: steps[index], index, total: steps.length };
}
