"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { invalidateEducationContent } from "@/entities/course";
import { getErrorMessage } from "@/shared/api";
import type { ContentAccessType } from "@/shared/ui/components";
import { materialsApi, materialDetailQueryOptions } from "../api";
import type { MaterialAccessType, MaterialId } from "../types";

/**
 * Меняет уровень доступа единичного материала. Бэкенд-эндпоинт PUT-семантичный
 * по {@code title/content/kind/accessType}, поэтому хук сначала тянет current detail
 * (или достаёт из react-query кеша, если он уже есть), мерджит {@code accessType}
 * и шлёт обратно. Используется в три-точечных меню по всему authoring-surface
 * (MaterialAuthorActions, SortableModuleItemCard, …).
 *
 * <p>Если AccessType совпал с текущим — мутация скипает запрос (toast не показывает).
 * Сделано в hook'е, не на бэке — чтобы не пнуть сервер пустым PATCH'ем на двойной клик.
 */
export function useChangeMaterialAccessType() {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: async ({
      materialId,
      accessType,
    }: {
      materialId: MaterialId;
      accessType: MaterialAccessType;
    }): Promise<{ materialId: MaterialId; changed: boolean }> => {
      const current = await queryClient.fetchQuery(materialDetailQueryOptions(materialId));
      if (current.accessType === accessType) {
        return { materialId, changed: false };
      }
      await materialsApi.updateMaterial({
        materialId,
        request: {
          title: current.title,
          content: current.content,
          kind: current.kind,
          accessType,
          // Чтобы sync media-binding не сорвал транзакцию: повторно передаём текущие
          // assetId — handler делает diff и видит «без изменений», bind/detach не дергает.
          videoId: current.videoId,
          previewId: current.imageId,
          // PUT-семантика quizId (#489): без него привязанный квиз отвяжется.
          quizId: current.quizId ?? null,
        },
      });
      return { materialId, changed: true };
    },
    onSuccess: async (result) => {
      if (result.changed) {
        toast.success("Уровень доступа обновлён");
        await invalidateEducationContent(queryClient);
      }
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка смены уровня доступа"));
    },
  });

  const setAccessType = async (materialId: MaterialId, accessType: ContentAccessType) => {
    await mutation.mutateAsync({ materialId, accessType: accessType as MaterialAccessType });
  };

  return {
    setAccessType,
    isPending: mutation.isPending,
  };
}
