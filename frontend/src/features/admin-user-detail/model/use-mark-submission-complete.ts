"use client";

import { adminCrossServiceQueryOptions } from "@/entities/admin-cross-service";
import { reviewSubmissionsApi } from "@/entities/review-submission";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

/**
 * Admin/author «Отметить выполненным» прямо со страницы управления пользователем.
 * Force-approve существующего submission'а (#383) — работает из любого статуса проверки,
 * включая застрявшие/gated сабмишены, которые НЕ попадают в инбокс проверок
 * (`ready_for_human_review=false`). После приёмки инвалидируем submissions-таб юзера.
 */
export function useMarkSubmissionComplete(userId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ courseId, submissionId }: { courseId: string; submissionId: string }) =>
      reviewSubmissionsApi.markComplete({ courseId, submissionId }),
    onSuccess: async () => {
      toast.success("Задание засчитано");
      await qc.invalidateQueries({
        queryKey: adminCrossServiceQueryOptions.getSubmissionsOptions(userId).queryKey,
      });
    },
    onError: (error) => toast.error(getErrorMessage(error, "Не удалось засчитать задание")),
  });
}
