"use client";

import { accessPlanApi } from "@/entities/access-plan";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

export function useRedeemInvite() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (token: string) => accessPlanApi.redeemInvite(token),
    onSuccess: () => {
      toast.success("Доступ открыт");
      queryClient.invalidateQueries({ queryKey: ["access", "me", "grants"] });
      queryClient.invalidateQueries({ queryKey: ["enrollment"] });
      queryClient.invalidateQueries({ queryKey: ["course-progress"] });
    },
    onError: (error) => toast.error(getErrorMessage(error, "Не удалось активировать инвайт")),
  });
}
