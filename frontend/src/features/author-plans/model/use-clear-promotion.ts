"use client";

import { accessPlanApi } from "@/entities/access-plan";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { myPlansKey } from "./keys";

export function useClearPromotion(planId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: () => accessPlanApi.clearPromotion(planId),
    onSuccess: async () => {
      toast.success("Акция убрана");
      await Promise.all([
        qc.invalidateQueries({ queryKey: myPlansKey }),
        qc.invalidateQueries({ queryKey: ["access", "plans", "public"] }),
      ]);
    },
    onError: (error) => toast.error(getErrorMessage(error, "Ошибка снятия акции")),
  });
}
