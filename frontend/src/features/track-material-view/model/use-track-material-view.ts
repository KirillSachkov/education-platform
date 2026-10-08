"use client";

import { useEffect } from "react";
import { useSession } from "next-auth/react";
import { getOrCreateAnonymousId } from "@/shared/lib/anonymous-id";
import { trackGrowthEvent } from "@/shared/analytics";
import { trackMaterialViewApi } from "./api";

interface UseTrackMaterialViewOptions {
  materialId: string | null | undefined;
  courseId?: string;
  /**
   * Если false — view не записывается. Используется чтобы не считать просмотр
   * у заблокированных материалов (anon видит заголовок, но не контент → не
   * считаем).
   */
  isAccessible?: boolean;
}

/**
 * Silent fire-and-forget трекер просмотра материала для счётчика «N просмотров».
 * Дёргается один раз на mount detail-страницы. Не показывает toast'ов, ошибки
 * глотает (счётчик не должен ломать UX). Issue #234.
 *
 * Auth user → <c>POST /progress/materials/{id}/track-view/</c> — silent,
 * НЕ помечает «изучено», НЕ каскадит прогресс курса, НЕ начисляет XP (issue #285).
 * Для явной отметки изученным используется отдельная мутация
 * <c>useMarkMaterialViewed</c> (кнопка «Отметить изученным»).
 *
 * Anonymous → <c>POST /progress/materials/{id}/anonymous-view/</c> с cookie-id
 * (идемпотентно по cookie+material).
 */
export function useTrackMaterialView({
  materialId,
  courseId,
  isAccessible = true,
}: UseTrackMaterialViewOptions) {
  const { status } = useSession();

  useEffect(() => {
    if (!materialId || !isAccessible) {
      return;
    }
    // Не запускаем пока сессия в "loading" — иначе может выстрелить дважды
    // (anonymous → authenticated) и счётчик чуть подпухнет на свежих юзерах.
    if (status === "loading") {
      return;
    }

    let cancelled = false;
    const run = async () => {
      try {
        if (status === "authenticated") {
          await trackMaterialViewApi.trackAuthView(materialId);
          if (courseId) {
            trackGrowthEvent(
              {
                name: "first_material_started",
                properties: {
                  material_id: materialId,
                  course_id: courseId,
                },
              },
              { once: "activation:first-material-started" },
            );
          }
        } else {
          const anonymousId = getOrCreateAnonymousId();
          if (!anonymousId || cancelled) return;
          await trackMaterialViewApi.trackAnonymousView(materialId, anonymousId);
        }
      } catch {
        // swallow — счётчик не должен ломать страницу
      }
    };
    void run();

    return () => {
      cancelled = true;
    };
  }, [courseId, materialId, status, isAccessible]);
}
