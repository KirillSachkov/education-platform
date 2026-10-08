"use client";

import { accessPlanApi } from "@/entities/access-plan";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { myPlansKey } from "./keys";

export function useArchivePlan() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (planId: string) => accessPlanApi.archivePlan(planId),
    onSuccess: async () => {
      toast.success("План архивирован");
      await qc.invalidateQueries({ queryKey: myPlansKey });
    },
    onError: (error) =>
      toast.error(getErrorMessage(error, "Не удалось архивировать план")),
  });
}
