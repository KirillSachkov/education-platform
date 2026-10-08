"use client";

import { accessPlanApi } from "@/entities/access-plan";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { myPlansKey } from "./keys";

export function usePublishPlan() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (planId: string) => accessPlanApi.publishPlan(planId),
    onSuccess: async () => {
      toast.success("План опубликован");
      await qc.invalidateQueries({ queryKey: myPlansKey });
    },
    onError: (error) =>
      toast.error(getErrorMessage(error, "Не удалось опубликовать план")),
  });
}
