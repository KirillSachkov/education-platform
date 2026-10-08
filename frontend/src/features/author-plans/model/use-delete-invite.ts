"use client";

import { accessPlanApi } from "@/entities/access-plan";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { planInvitesKey } from "./keys";

export function useDeleteInvite(planId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (inviteId: string) => accessPlanApi.deleteInvite(inviteId),
    onSuccess: async () => {
      toast.success("Ссылка удалена");
      await qc.invalidateQueries({ queryKey: planInvitesKey(planId) });
    },
    onError: (error) =>
      toast.error(getErrorMessage(error, "Не удалось удалить ссылку")),
  });
}
