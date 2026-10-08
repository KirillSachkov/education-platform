"use client";

import { accessPlanApi } from "@/entities/access-plan";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { planInvitesKey } from "./keys";

export function useRevokeInvite(planId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (inviteId: string) => accessPlanApi.revokeInvite(inviteId),
    onSuccess: async () => {
      toast.success("Инвайт отозван");
      await qc.invalidateQueries({ queryKey: planInvitesKey(planId) });
    },
    onError: (error) =>
      toast.error(getErrorMessage(error, "Не удалось отозвать инвайт")),
  });
}
