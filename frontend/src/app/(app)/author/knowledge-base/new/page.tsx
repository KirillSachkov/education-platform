"use client";

import { materialsApi } from "@/entities/material";
import { getErrorMessage } from "@/shared/api";
import { routes } from "@/shared/config/routes";
import { Icons } from "@/shared/ui/icons";
import { useMutation } from "@tanstack/react-query";
import { useRouter, useSearchParams } from "next/navigation";
import { useEffect, useRef } from "react";
import { toast } from "sonner";

/**
 * YouTube-style flow: на mount сразу создаём draft-материал и редиректим в edit.
 * Все правки и upload'ы идут уже на реальный entity — refresh страницы не теряет
 * прогресс. Старые URL-параметры (`courseId`, `courseSlug`, `moduleId`,
 * `collectionId`, `sectionId`) пробрасываются в edit-page как query, чтобы
 * after-create flow (attach to collection, return to course-builder) работал
 * по-прежнему.
 */
export default function AuthorMaterialCreatePage() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const courseId = searchParams.get("courseId") ?? undefined;
  const courseSlug = searchParams.get("courseSlug") ?? undefined;
  const moduleId = searchParams.get("moduleId") ?? undefined;
  const collectionId = searchParams.get("collectionId") ?? undefined;
  const sectionId = searchParams.get("sectionId") ?? undefined;

  // React 19 strict-mode dev: useEffect fires twice. Guard idempotency at the
  // effect level — без этого создаются два DRAFT-материала на одно открытие.
  const triggeredRef = useRef(false);

  const createDraft = useMutation({
    mutationFn: () =>
      materialsApi.createDraftMaterial({
        courseId,
        moduleId,
        // Если открыли из picker'а подборки — атомарно прикрепляем материал к секции
        // в той же транзакции на бэке. Без этого юзер вернётся в редактор подборки и
        // не увидит свой материал (был бы orphan в базе знаний).
        collectionId,
        sectionId,
      }),
    onSuccess: (materialId) => {
      // sectionId не нужен в edit-странице — сама привязка к секции уже выполнена
      // в backend-транзакции. Пробрасываем только collectionId, чтобы header показал
      // «← Назад к подборке».
      router.replace(
        routes.authorMaterialEdit(materialId, { courseId, courseSlug, collectionId }),
      );
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Не удалось создать черновик"));
      router.replace(routes.authorKnowledgeBase);
    },
  });

  const { mutate: triggerCreateDraft } = createDraft;
  useEffect(() => {
    if (triggeredRef.current) return;
    triggeredRef.current = true;
    triggerCreateDraft();
  }, [triggerCreateDraft]);

  return (
    <div className="flex h-[60vh] items-center justify-center">
      <div className="flex flex-col items-center gap-3 text-muted-foreground">
        <Icons.loading className="h-6 w-6 animate-spin" />
        <span className="text-sm">Создаём черновик…</span>
      </div>
    </div>
  );
}
