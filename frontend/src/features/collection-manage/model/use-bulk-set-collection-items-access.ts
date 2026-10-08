"use client";

import { collectionsApi } from "@/entities/collection";
import { invalidateEducationContent } from "@/entities/course";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

/**
 * Bulk-смена AccessType у всех материалов подборки. Toast после success
 * проговаривает что произошло: "N обновлено, M уже было на этом уровне,
 * K чужих материалов пропущено" — чтобы автор понимал blast radius.
 */
export function useBulkSetCollectionItemsAccess() {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: collectionsApi.bulkSetItemsAccessType,
    onSuccess: async (result) => {
      const parts: string[] = [];
      if (result.updatedCount > 0) parts.push(`обновлено: ${result.updatedCount}`);
      if (result.skippedCount > 0) parts.push(`уже на этом уровне: ${result.skippedCount}`);
      if (result.skippedNotOwnedCount > 0)
        parts.push(`чужих пропущено: ${result.skippedNotOwnedCount}`);

      const summary = parts.length > 0 ? parts.join(", ") : "ничего не изменилось";
      toast.success(`Доступ материалов · ${summary}`);

      await invalidateEducationContent(queryClient);
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Не удалось обновить доступ материалов"));
    },
  });

  return {
    bulkSetAccess: mutation.mutateAsync,
    isPending: mutation.isPending,
  };
}
