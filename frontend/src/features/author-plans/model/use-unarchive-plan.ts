"use client";

import { accessPlanApi } from "@/entities/access-plan";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { myPlansKey } from "./keys";

export function useUnarchivePlan() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (planId: string) => accessPlanApi.unarchivePlan(planId),
    onSuccess: async () => {
      toast.success("План восстановлен");
      await qc.invalidateQueries({ queryKey: myPlansKey });
    },
    onError: (error) =>
      toast.error(getErrorMessage(error, "Не удалось восстановить план")),
  });
}
