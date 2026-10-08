"use client";

import { accessPlanApi, type CreateInviteRequest } from "@/entities/access-plan";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { planInvitesKey } from "./keys";

export function useCreateInvite(planId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: CreateInviteRequest) =>
      accessPlanApi.createInvite(planId, req),
    onSuccess: async () => {
      toast.success("Инвайт создан");
      await qc.invalidateQueries({ queryKey: planInvitesKey(planId) });
    },
    onError: (error) => toast.error(getErrorMessage(error, "Ошибка создания инвайта")),
  });
}
