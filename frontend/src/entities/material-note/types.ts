/** Максимальная длина заметки — зеркалит `MaterialNote.MAX_CONTENT_LENGTH` на бэкенде. */
export const MATERIAL_NOTE_MAX_LENGTH = 20000;

/** Личная заметка пользователя к материалу (видна только владельцу). Issue #465. */
export interface MaterialNoteDto {
  materialId: string;
  content: string;
  createdAt: string;
  updatedAt: string;
}
