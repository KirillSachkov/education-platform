"use client";

import {
  trainerAdminContentApi,
  trainerAdminContentQueryOptions,
  type AddTrainerTopicBankBody,
  type UpdateTrainerTopicBankBody,
} from "@/entities/trainer-admin-content";
import { getErrorMessage } from "@/shared/api";
import { type QueryClient, useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

/**
 * Сносит банки конкретной темы + admin-список тем (меняется bankCount, влияет
 * на возможность удалить тему) + студенческие проекции тем (freemium-флаги
 * `hasFreeBank`/`isLocked` зависят от состава банков).
 */
async function invalidateBanks(queryClient: QueryClient, topicId: string) {
  await Promise.all([
    queryClient.invalidateQueries({
      queryKey: trainerAdminContentQueryOptions.banksKey(topicId),
    }),
    queryClient.invalidateQueries({
      queryKey: [trainerAdminContentQueryOptions.topicsBaseKey],
    }),
    queryClient.invalidateQueries({ queryKey: ["trainer-topics"] }),
  ]);
}

interface AddBankInput {
  topicId: string;
  body: AddTrainerTopicBankBody;
}

/** Создать пустой банк вопросов под темой (#623 — вопросы добавляются отдельно). */
export function useAddTrainerTopicBank() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ topicId, body }: AddBankInput) =>
      trainerAdminContentApi.addTopicBank(topicId, body),
    onSuccess: async (_id, { topicId }) => {
      toast.success("Банк создан");
      await invalidateBanks(queryClient, topicId);
    },
    onError: (error) => toast.error(getErrorMessage(error, "Ошибка создания банка")),
  });
}

interface UpdateBankInput {
  topicId: string;
  bankId: string;
  body: UpdateTrainerTopicBankBody;
}

/** Обновить банк (tier/difficulty/purpose). TopicId immutable. */
export function useUpdateTrainerTopicBank() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ bankId, body }: UpdateBankInput) =>
      trainerAdminContentApi.updateTopicBank(bankId, body),
    onSuccess: async (_void, { topicId }) => {
      toast.success("Банк обновлён");
      await invalidateBanks(queryClient, topicId);
    },
    onError: (error) => toast.error(getErrorMessage(error, "Ошибка сохранения банка")),
  });
}

interface SetBankTierInput {
  topicId: string;
  bankId: string;
  tier: string;
}

/** Быстрый toggle tier банка (FREE/PAID). */
export function useSetTrainerTopicBankTier() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ bankId, tier }: SetBankTierInput) =>
      trainerAdminContentApi.setTopicBankTier(bankId, tier),
    onSuccess: async (_void, { topicId }) => {
      await invalidateBanks(queryClient, topicId);
    },
    onError: (error) => toast.error(getErrorMessage(error, "Ошибка смены доступа банка")),
  });
}

interface DeleteBankInput {
  topicId: string;
  bankId: string;
}

/** Удалить банк (вопросы каскадятся). */
export function useDeleteTrainerTopicBank() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ bankId }: DeleteBankInput) => trainerAdminContentApi.deleteTopicBank(bankId),
    onSuccess: async (_void, { topicId }) => {
      toast.success("Банк удалён");
      await invalidateBanks(queryClient, topicId);
    },
    onError: (error) => toast.error(getErrorMessage(error, "Ошибка удаления банка")),
  });
}
