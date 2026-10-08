"use client";

import {
  trainerAdminContentApi,
  trainerAdminContentQueryOptions,
  type CreateTrainerTopicBody,
  type UpdateTrainerTopicBody,
} from "@/entities/trainer-admin-content";
import { getErrorMessage } from "@/shared/api";
import { type QueryClient, useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

/**
 * Сносит все admin-проекции тем (список + карточки) — мутация может менять
 * любой из трек-фильтров, поэтому инвалидируем весь baseKey. Публикация/смена
 * темы видна студентам — также сносим студенческий список тем/прогресс.
 */
async function invalidateTopics(queryClient: QueryClient) {
  await Promise.all([
    queryClient.invalidateQueries({
      queryKey: [trainerAdminContentQueryOptions.topicsBaseKey],
    }),
    queryClient.invalidateQueries({
      queryKey: [trainerAdminContentQueryOptions.topicBaseKey],
    }),
    queryClient.invalidateQueries({ queryKey: ["trainer-topics"] }),
    queryClient.invalidateQueries({ queryKey: ["trainer-tracks"] }),
  ]);
}

/** Создать тему тренажёра (DRAFT). Возвращает id для авто-выбора. */
export function useCreateTrainerTopic() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateTrainerTopicBody) => trainerAdminContentApi.createTopic(body),
    onSuccess: async () => {
      toast.success("Тема создана");
      await invalidateTopics(queryClient);
    },
    onError: (error) => toast.error(getErrorMessage(error, "Ошибка создания темы")),
  });
}

interface UpdateTopicInput {
  topicId: string;
  body: UpdateTrainerTopicBody;
}

/** Обновить детали темы + маппинг на курс. Slug immutable. */
export function useUpdateTrainerTopic() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ topicId, body }: UpdateTopicInput) =>
      trainerAdminContentApi.updateTopic(topicId, body),
    onSuccess: async () => {
      toast.success("Тема обновлена");
      await invalidateTopics(queryClient);
    },
    onError: (error) => toast.error(getErrorMessage(error, "Ошибка сохранения темы")),
  });
}

/** Опубликовать / снять с публикации тему (toggle на стороне вызывающего). */
export function useSetTrainerTopicPublished() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ topicId, publish }: { topicId: string; publish: boolean }) =>
      publish
        ? trainerAdminContentApi.publishTopic(topicId)
        : trainerAdminContentApi.unpublishTopic(topicId),
    onSuccess: async (_void, { publish }) => {
      toast.success(publish ? "Тема опубликована" : "Тема снята с публикации");
      await invalidateTopics(queryClient);
    },
    onError: (error) => toast.error(getErrorMessage(error, "Ошибка смены статуса темы")),
  });
}

/**
 * Удалить тему. Бэкенд отдаёт 409 `trainer.topic.has.banks`, если к теме
 * привязаны банки — сообщение бэка уже человекочитаемо, getErrorMessage его
 * покажет («Сначала отвяжите банки»).
 */
export function useDeleteTrainerTopic() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (topicId: string) => trainerAdminContentApi.deleteTopic(topicId),
    onSuccess: async () => {
      toast.success("Тема удалена");
      await invalidateTopics(queryClient);
    },
    onError: (error) => toast.error(getErrorMessage(error, "Ошибка удаления темы")),
  });
}
