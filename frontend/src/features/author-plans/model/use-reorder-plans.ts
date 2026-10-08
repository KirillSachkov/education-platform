"use client";

import { accessPlanApi } from "@/entities/access-plan";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { myPlansKey } from "./keys";

export function useReorderPlans() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (orders: Array<{ planId: string; displayOrder: number }>) =>
      accessPlanApi.reorderPlans(orders),
    onSuccess: async () => {
      await qc.invalidateQueries({ queryKey: myPlansKey });
    },
    onError: (error) =>
      toast.error(getErrorMessage(error, "Не удалось изменить порядок планов")),
  });
}
