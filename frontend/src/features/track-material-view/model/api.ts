import { apiClient, type Envelope } from "@/shared/api";

/**
 * API-вызовы фичи «track material view» — silent fire-and-forget по mount'у
 * детали материала. Считает уникальные просмотры в ProgressService:
 * <c>material_views</c> для авторизованных + <c>anonymous_material_views</c>
 * для анонимов по cookie. Issue #234.
 *
 * ВАЖНО (issue #285): silent track НЕ должен помечать материал как «изученный».
 * Auth-эндпоинт <c>/track-view/</c> отдельный от <c>/view/</c> (последний — это
 * явная кнопка «Отметить изученным» с cascade на module_item_progress и XP).
 */
export const trackMaterialViewApi = {
  /**
   * Auth users — silent <c>POST /progress/materials/{id}/track-view/</c>.
   * Идемпотентно по паре (UserId, MaterialId). НЕ каскадит прогресс курса,
   * НЕ начисляет XP, НЕ помечает «изучено». Если у пользователя уже стоит
   * is_completed=true (он нажимал «Отметить изученным» раньше) — track-view
   * не понижает состояние. Issue #285.
   */
  trackAuthView: async (materialId: string) => {
    const res = await apiClient.post<Envelope<void>>(
      `/progress/materials/${materialId}/track-view/`,
    );
    return res.data;
  },

  /**
   * Anonymous visitors — идемпотентный
   * <c>POST /progress/materials/{id}/anonymous-view/</c>. AllowAnonymous +
   * per-IP rate-limit. Дедуп — по UUID v4 из cookie <c>plu_anon_id</c>.
   */
  trackAnonymousView: async (materialId: string, anonymousId: string) => {
    const res = await apiClient.post<Envelope<void>>(
      `/progress/materials/${materialId}/anonymous-view/`,
      { anonymousId },
    );
    return res.data;
  },
};
