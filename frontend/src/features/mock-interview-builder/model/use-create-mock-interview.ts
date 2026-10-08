"use client";

import { mockInterviewsApi, mockInterviewsQueryOptions } from "@/entities/mock-interview";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { slugifyMockInterview } from "./slug";

/**
 * Создание мок-собеса в DRAFT (#585): только title — slug генерируется из
 * названия (с рандомным суффиксом для кириллицы / уникальности), вопросы
 * наполняются в редакторе. Возвращает id для авто-раскрытия редактора.
 */
export function useCreateMockInterview() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (title: string) =>
      mockInterviewsApi.create({ slug: slugifyMockInterview(title), title }),
    onSuccess: async () => {
      toast.success("Мок-собес создан");
      await queryClient.invalidateQueries({ queryKey: mockInterviewsQueryOptions.manageKey() });
    },
    onError: (error) => toast.error(getErrorMessage(error, "Ошибка создания мок-собеса")),
  });
}
