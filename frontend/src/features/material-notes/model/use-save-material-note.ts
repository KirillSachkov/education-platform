import { materialNoteQueryOptions, materialNotesApi } from "@/entities/material-note";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

/**
 * Upsert личной заметки к материалу (autosave). Без success-toast'а — сохранение
 * фоновое, индикатор «Сохранено» рисует сам блок. Кеш обновляется ответом PUT,
 * чтобы не дёргать лишний GET после каждого дебаунс-сейва. Issue #465.
 */
export function useSaveMaterialNote(materialId: string) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (content: string) => materialNotesApi.upsertMaterialNote(materialId, content),
    onSuccess: (note) => {
      queryClient.setQueryData(materialNoteQueryOptions.noteKey(materialId), note);
    },
    onError: (error) => toast.error(getErrorMessage(error, "Не удалось сохранить заметку")),
  });
}
