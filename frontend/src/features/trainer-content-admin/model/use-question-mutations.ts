"use client";

import { trainerAdminContentQueryOptions } from "@/entities/trainer-admin-content";
import {
  trainerQuestionAdminApi,
  trainerQuestionAdminQueryOptions,
  type TrainerQuestionInput,
} from "@/entities/trainer-question-admin";
import { getErrorMessage } from "@/shared/api";
import { type QueryClient, useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

/**
 * Сносит вопросы банка (список редактора) + банки темы (меняется questionCount) +
 * студенческие проекции тем (freemium-флаги зависят от наличия вопросов).
 */
async function invalidateBankQuestions(
  queryClient: QueryClient,
  bankId: string,
  topicId: string,
) {
  await Promise.all([
    queryClient.invalidateQueries({
      queryKey: trainerQuestionAdminQueryOptions.bankQuestionsKey(bankId),
    }),
    queryClient.invalidateQueries({
      queryKey: trainerAdminContentQueryOptions.banksKey(topicId),
    }),
    queryClient.invalidateQueries({ queryKey: ["trainer-topics"] }),
  ]);
}

interface CreateQuestionInput {
  bankId: string;
  topicId: string;
  body: TrainerQuestionInput;
}

/** Создать вопрос в банке. */
export function useCreateTrainerQuestion() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ bankId, body }: CreateQuestionInput) =>
      trainerQuestionAdminApi.createQuestion(bankId, body),
    onSuccess: async (_id, { bankId, topicId }) => {
      toast.success("Вопрос добавлен");
      await invalidateBankQuestions(queryClient, bankId, topicId);
    },
    onError: (error) => toast.error(getErrorMessage(error, "Ошибка добавления вопроса")),
  });
}

interface UpdateQuestionInput {
  questionId: string;
  bankId: string;
  topicId: string;
  body: TrainerQuestionInput;
}

/** Обновить вопрос (полная замена + варианты). */
export function useUpdateTrainerQuestion() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ questionId, body }: UpdateQuestionInput) =>
      trainerQuestionAdminApi.updateQuestion(questionId, body),
    onSuccess: async (_id, { bankId, topicId }) => {
      toast.success("Вопрос обновлён");
      await invalidateBankQuestions(queryClient, bankId, topicId);
    },
    onError: (error) => toast.error(getErrorMessage(error, "Ошибка сохранения вопроса")),
  });
}

interface DeleteQuestionInput {
  questionId: string;
  bankId: string;
  topicId: string;
}

/** Удалить вопрос (варианты каскадятся). */
export function useDeleteTrainerQuestion() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ questionId }: DeleteQuestionInput) =>
      trainerQuestionAdminApi.deleteQuestion(questionId),
    onSuccess: async (_void, { bankId, topicId }) => {
      toast.success("Вопрос удалён");
      await invalidateBankQuestions(queryClient, bankId, topicId);
    },
    onError: (error) => toast.error(getErrorMessage(error, "Ошибка удаления вопроса")),
  });
}
