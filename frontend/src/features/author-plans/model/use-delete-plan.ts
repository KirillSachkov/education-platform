"use client";

import { accessPlanApi } from "@/entities/access-plan";
import { getErrorMessage } from "@/shared/api";
import { routes } from "@/shared/config/routes";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useRouter } from "next/navigation";
import { toast } from "sonner";
import { myPlansKey } from "./keys";

// Полное удаление плана. На успех — уводим со страницы плана (она больше не существует)
// обратно к списку. Guard (оплаты / активные гранты) приходит 409 → показываем сообщение бэка.
export function useDeletePlan() {
  const qc = useQueryClient();
  const router = useRouter();
  return useMutation({
    mutationFn: (planId: string) => accessPlanApi.deletePlan(planId),
    onSuccess: async () => {
      toast.success("План удалён");
      // Инвалидируем И приватный список автора, И публичный каталог /pricing — иначе
      // удалённый опубликованный план висит на /pricing до истечения staleTime
      // (зеркалит use-set-promotion).
      await Promise.all([
        qc.invalidateQueries({ queryKey: myPlansKey }),
        qc.invalidateQueries({ queryKey: ["access", "plans", "public"] }),
      ]);
      router.push(routes.authorPlans);
    },
    onError: (error) => toast.error(getErrorMessage(error, "Не удалось удалить план")),
  });
}
