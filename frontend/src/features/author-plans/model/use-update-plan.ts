"use client";

import { accessPlanApi, type UpdatePlanRequest } from "@/entities/access-plan";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { myPlansKey } from "./keys";

export function useUpdatePlan(planId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: UpdatePlanRequest) => accessPlanApi.updatePlan(planId, req),
    onSuccess: async () => {
      toast.success("План обновлён");
      await qc.invalidateQueries({ queryKey: myPlansKey });
    },
    onError: (error) => toast.error(getErrorMessage(error, "Ошибка обновления плана")),
  });
}
