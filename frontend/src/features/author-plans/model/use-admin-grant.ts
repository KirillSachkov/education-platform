"use client";

import { accessPlanApi, type AdminGrantRequest } from "@/entities/access-plan";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { planGrantsKey } from "./keys";

export function useAdminGrant(planId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: AdminGrantRequest) => accessPlanApi.adminGrant(req),
    onSuccess: async () => {
      toast.success("Доступ выдан");
      await qc.invalidateQueries({ queryKey: planGrantsKey(planId) });
    },
    onError: (error) =>
      toast.error(getErrorMessage(error, "Не удалось выдать доступ")),
  });
}
