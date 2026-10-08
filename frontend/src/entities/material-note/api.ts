import { apiClient, ErrorType, isEnvelopeError, type Envelope } from "@/shared/api";
import { queryOptions } from "@tanstack/react-query";
import type { MaterialNoteDto } from "./types";

export const materialNotesApi = {
  /** Возвращает `null`, если заметки ещё нет (бэкенд отвечает 404). */
  getMaterialNote: async (
    materialId: string,
    { signal }: { signal?: AbortSignal } = {},
  ): Promise<MaterialNoteDto | null> => {
    try {
      const res = await apiClient.get<Envelope<MaterialNoteDto>>(
        `/progress/materials/${materialId}/note/`,
        { signal },
      );
      return res.data.result ?? null;
    } catch (error) {
      if (isEnvelopeError(error) && error.type === ErrorType.NOT_FOUND) {
        return null;
      }
      throw error;
    }
  },

  upsertMaterialNote: async (materialId: string, content: string) => {
    const res = await apiClient.put<Envelope<MaterialNoteDto>>(
      `/progress/materials/${materialId}/note/`,
      { content },
    );
    return res.data.result!;
  },

  deleteMaterialNote: async (materialId: string) => {
    await apiClient.delete<Envelope<string>>(`/progress/materials/${materialId}/note/`);
  },
};

export const materialNoteQueryOptions = {
  baseKey: "material-notes",

  noteKey: (materialId: string) => [materialNoteQueryOptions.baseKey, materialId] as const,

  noteOptions: (materialId: string) =>
    queryOptions({
      queryKey: materialNoteQueryOptions.noteKey(materialId),
      queryFn: ({ signal }) => materialNotesApi.getMaterialNote(materialId, { signal }),
      staleTime: 60_000,
    }),
};
