import { invalidateEducationContent } from "@/entities/course";
import { modulesApi } from "@/entities/module";
import type { TransferModuleItemRequest } from "@/entities/module";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

export function useTransferModuleItem(_courseId: string) {
  const queryClient = useQueryClient();

  // No optimistic cache update. The optimistic path remounted the dragged item
  // under a different parent (source ModuleCard → target ModuleCard) in the
  // same microtask as dnd-kit's drag-end commit, which left the source module's
  // useSortable registrations in an inconsistent state — subsequent intra-module
  // drags in either module would no longer trigger move mutations (#213).
  // The trade-off is a brief flash (~200 ms) where the item snaps back to the
  // source module before the refetch settles in the target. DOM restoration
  // in module-list's onDragEnd still prevents the DOMException.
  const mutation = useMutation({
    mutationFn: ({
      sourceModuleId,
      referenceId,
      request,
    }: {
      sourceModuleId: string;
      referenceId: string;
      request: TransferModuleItemRequest;
    }) => modulesApi.transferItem({ sourceModuleId, referenceId, request }),
    onSuccess: async () => {
      await invalidateEducationContent(queryClient);
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка переноса в другой модуль"));
    },
  });

  return {
    transferItem: mutation.mutateAsync,
    isPending: mutation.isPending,
  };
}
