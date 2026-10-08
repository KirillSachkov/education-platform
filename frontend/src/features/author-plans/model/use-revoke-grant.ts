"use client";

import { accessPlanApi } from "@/entities/access-plan";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { planGrantsKey } from "./keys";

export function useRevokeGrant(planId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ grantId, reason }: { grantId: string; reason?: string }) =>
      accessPlanApi.revokeGrant(grantId, reason),
    onSuccess: async () => {
      toast.success("Доступ отозван");
      await qc.invalidateQueries({ queryKey: planGrantsKey(planId) });
    },
    onError: (error) =>
      toast.error(getErrorMessage(error, "Не удалось отозвать доступ")),
  });
}
