import type { LockReason } from "@/shared/lib/lock-copy";
import type { MaterialKind } from "@/entities/material";

/**
 * Закреп для home-дашборда студента (`GET /access/me/home-pins/`). Зеркалит backend
 * `HomePinDto` (AccessService.Contracts/HomePins/HomePinResponses.cs). `href` приходит
 * как `/knowledge-base/{materialId}`; UI всё равно строит ссылку сам через
 * `routes.knowledgeBaseMaterial(materialId)` — единый источник правды для роута.
 */
export type HomePinDto = {
  materialId: string;
  title: string;
  /** Material kind discriminator (ARTICLE | VIDEO | NOTE | STREAM). */
  kind: MaterialKind;
  thumbnailUrl: string | null;
  note: string | null;
  isAccessible: boolean;
  lockReason: LockReason | null;
  /** Backend-supplied `/knowledge-base/{materialId}`; UI строит ссылку через routes (см. выше). */
  href: string;
};

/**
 * Author-side строка списка закрепов плана (`GET /access/plans/{id}/home-pins/`).
 * Зеркалит backend `HomePinListItemDto`.
 */
export type HomePinListItemDto = {
  pinId: string;
  materialId: string;
  title: string;
  note: string | null;
  sortKey: string;
};

/** `POST /access/plans/{id}/home-pins/` — закрепить материал (в конец списка). */
export type AddHomePinRequest = {
  materialId: string;
  note: string | null;
};

/** `PATCH /access/plans/{id}/home-pins/{pinId}/` — обновить заметку (null очищает). */
export type UpdateHomePinNoteRequest = {
  note: string | null;
};

/**
 * `POST /access/plans/{id}/home-pins/{pinId}/order/` — переупорядочить закреп.
 * Указываем соседей новой позиции для вычисления fractional SortKey.
 */
export type ReorderHomePinRequest = {
  beforeId: string | null;
  afterId: string | null;
};
