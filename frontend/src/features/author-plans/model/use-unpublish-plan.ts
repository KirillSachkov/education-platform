"use client";

import { accessPlanApi } from "@/entities/access-plan";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { myPlansKey } from "./keys";

export function useUnpublishPlan() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (planId: string) => accessPlanApi.unpublishPlan(planId),
    onSuccess: async () => {
      toast.success("План снят с публикации");
      await qc.invalidateQueries({ queryKey: myPlansKey });
    },
    onError: (error) =>
      toast.error(getErrorMessage(error, "Не удалось снять с публикации")),
  });
}
