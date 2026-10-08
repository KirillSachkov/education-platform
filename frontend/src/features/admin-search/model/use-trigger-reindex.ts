"use client";

import { apiClient } from "@/shared/api/axios-instance";
import { getErrorMessage } from "@/shared/api/errors";
import { useMutation } from "@tanstack/react-query";
import { toast } from "sonner";

interface ReindexResponse {
  requestId: string;
  entityType: string | null;
  requestedAtUtc: string;
}

export function useTriggerReindex() {
  return useMutation({
    mutationFn: async () => {
      const res = await apiClient.post<{ result: ReindexResponse }>(
        "/search/admin/reindex/",
        {},
      );
      return res.data.result;
    },
    onSuccess: (data) => {
      toast.success(
        `Переиндексация запущена — ID запроса ${data.requestId.slice(0, 8)}…`,
      );
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Не удалось запустить переиндексацию"));
    },
  });
}
