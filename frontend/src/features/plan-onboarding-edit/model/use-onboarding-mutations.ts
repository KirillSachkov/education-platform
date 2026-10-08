"use client";

import {
  type AddMarkdownStepRequest,
  type ReorderStepRequest,
  type UpdateMarkdownStepRequest,
  planOnboardingApi,
} from "@/entities/plan-onboarding";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

const flowKey = (planId: string) => ["plan-onboarding", "flow", planId] as const;

export function useSetEnabled(planId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (isEnabled: boolean) =>
      planOnboardingApi.setEnabled(planId, { isEnabled }),
    onSuccess: async (_, variables) => {
      toast.success(variables ? "Онбординг включён" : "Онбординг выключен");
      await queryClient.invalidateQueries({ queryKey: flowKey(planId) });
    },
    onError: (error) => toast.error(getErrorMessage(error, "Не удалось обновить онбординг")),
  });
}

export function useAddMarkdownStep(planId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (request: AddMarkdownStepRequest) =>
      planOnboardingApi.addMarkdownStep(planId, request),
    onSuccess: async () => {
      toast.success("Шаг добавлен");
      await queryClient.invalidateQueries({ queryKey: flowKey(planId) });
    },
    onError: (error) => toast.error(getErrorMessage(error, "Не удалось создать шаг")),
  });
}

export function useUpdateMarkdownStep(planId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ stepId, request }: { stepId: string; request: UpdateMarkdownStepRequest }) =>
      planOnboardingApi.updateMarkdownStep(planId, stepId, request),
    onSuccess: async () => {
      toast.success("Шаг обновлён");
      await queryClient.invalidateQueries({ queryKey: flowKey(planId) });
    },
    onError: (error) => toast.error(getErrorMessage(error, "Не удалось обновить шаг")),
  });
}

export function useDeleteStep(planId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (stepId: string) => planOnboardingApi.deleteStep(planId, stepId),
    onSuccess: async () => {
      toast.success("Шаг удалён");
      await queryClient.invalidateQueries({ queryKey: flowKey(planId) });
    },
    onError: (error) => toast.error(getErrorMessage(error, "Не удалось удалить шаг")),
  });
}

export function useReorderStep(planId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ stepId, request }: { stepId: string; request: ReorderStepRequest }) =>
      planOnboardingApi.reorderStep(planId, stepId, request),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: flowKey(planId) });
    },
    onError: (error) => toast.error(getErrorMessage(error, "Не удалось переупорядочить шаг")),
  });
}

export function useSetStepIsSkippable(planId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ stepId, isSkippable }: { stepId: string; isSkippable: boolean }) =>
      planOnboardingApi.setStepIsSkippable(planId, stepId, isSkippable),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: flowKey(planId) });
    },
    onError: (error) =>
      toast.error(getErrorMessage(error, "Не удалось изменить настройку шага")),
  });
}

export function useToggleGithubReviewAppStep(planId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (isEnabled: boolean) =>
      planOnboardingApi.toggleGithubReviewAppStep(planId, { isEnabled }),
    onSuccess: async (_, isEnabled) => {
      toast.success(isEnabled ? "Шаг AI-проверки добавлен" : "Шаг AI-проверки удалён");
      await queryClient.invalidateQueries({ queryKey: flowKey(planId) });
    },
    onError: (error) =>
      toast.error(getErrorMessage(error, "Не удалось обновить шаг AI-проверки")),
  });
}

export function useResetAllOnboardings(planId: string) {
  return useMutation({
    mutationFn: () => planOnboardingApi.resetAllOnboardings(planId),
    onSuccess: (data) => {
      const count = data.result?.resetCount ?? 0;
      toast.success(
        count > 0
          ? `Онбординг перезапущен: ${count} ${onboardingsLabel(count)}`
          : "Никого не нужно перезапускать",
      );
    },
    onError: (error) => toast.error(getErrorMessage(error, "Не удалось перезапустить онбординг")),
  });
}

function onboardingsLabel(count: number): string {
  const lastTwo = count % 100;
  if (lastTwo >= 11 && lastTwo <= 14) return "учеников";
  const last = count % 10;
  if (last === 1) return "ученик";
  if (last >= 2 && last <= 4) return "ученика";
  return "учеников";
}
