"use client";

import { accessPlanApi, type SetPromotionRequest } from "@/entities/access-plan";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { myPlansKey } from "./keys";

export function useSetPromotion(planId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: SetPromotionRequest) => accessPlanApi.setPromotion(planId, req),
    onSuccess: async () => {
      toast.success("Акция включена");
      await Promise.all([
        qc.invalidateQueries({ queryKey: myPlansKey }),
        // Витрина /pricing кешируется отдельно — сбрасываем, чтобы новая цена
        // появилась сразу, а не по истечении staleTime.
        qc.invalidateQueries({ queryKey: ["access", "plans", "public"] }),
      ]);
    },
    onError: (error) => toast.error(getErrorMessage(error, "Ошибка установки акции")),
  });
}
