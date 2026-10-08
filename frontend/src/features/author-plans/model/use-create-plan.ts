"use client";

import { accessPlanApi, type CreatePlanRequest } from "@/entities/access-plan";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { myPlansKey } from "./keys";

export function useCreatePlan() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: CreatePlanRequest) => accessPlanApi.createPlan(req),
    onSuccess: async () => {
      // План создаётся как черновик (is_public=false). Подсказываем, что нужен
      // publish, чтобы он появился в /pricing и стал invitable — иначе автор
      // недоумевает, почему план «не виден» после создания.
      toast.success(
        "План создан в черновиках. Опубликуйте, чтобы он стал доступен ученикам",
      );
      await qc.invalidateQueries({ queryKey: myPlansKey });
    },
    onError: (error) => toast.error(getErrorMessage(error, "Ошибка создания плана")),
  });
}
