"use client";

import type { ReactNode } from "react";
import {
  getMaterialKindBadge,
  getMaterialStatusBadge,
  materialDetailQueryOptions,
  type MaterialDetailDto,
} from "@/entities/material";
import { routes } from "@/shared/config/routes";
import { NotFoundFallback } from "@/shared/ui/components/not-found-fallback";
import { Badge } from "@/shared/ui/kit/badge";
import { Button } from "@/shared/ui/kit/button";
import { ErrorCard } from "@/shared/ui/kit/error-card";
import { useQuery } from "@tanstack/react-query";
import { Loader2, Save } from "lucide-react";
import { useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { MaterialAuthorActions } from "./material-author-actions";
import { useCreateMaterial } from "../model/use-create-material";
import { useUpdateMaterial } from "../model/use-update-material";

export interface MaterialEditorFormRenderProps {
  mode: "create" | "edit";
  materialId?: string;
  material: MaterialDetailDto | null | undefined;
  formId: string;
  defaultAccessType: "PUBLIC" | "ENROLLED";
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  onSubmit: (values: any) => Promise<string | void> | string | void;
  onAfterSubmit: (materialId: string | undefined) => void;
  isPending: boolean;
  /**
   * Auto-save handler — задан только в edit-mode. Caller (форма) триггерит этот
   * обработчик с debounce'ом при dirty form, страница сама ходит в backend.
   */
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  onAutoSave?: (values: any) => Promise<void>;
}

interface MaterialEditorPageProps {
  mode: "create" | "edit";
  materialId?: string;
  /** Optional course context — when creating new material from course builder */
  courseId?: string;
  courseSlug?: string;
  moduleId?: string;
  /**
   * Optional override for the post-create navigation. Receives the new material id and
   * may perform side-effects (e.g. attach to a collection section) before returning the
   * redirect URL. Falling back to `undefined` defers to the default redirect.
   */
  afterCreateRedirect?: (materialId: string) => Promise<string | undefined> | string | undefined;
  /** Optional overrides for the back/cancel destination — defaults to the course builder or knowledge base. */
  backHrefOverride?: string;
  backLabelOverride?: string;
  /** Render prop for the material form — avoids cross-slice dependency on material-edit */
  renderForm: (props: MaterialEditorFormRenderProps) => ReactNode;
}

/** Standalone materials default to PUBLIC; course-context materials default to ENROLLED */
function getDefaultAccessType(courseId?: string): "PUBLIC" | "ENROLLED" {
  return courseId ? "ENROLLED" : "PUBLIC";
}

const FORM_ID = "material-editor-form";

export function MaterialEditorPage({
  mode,
  materialId,
  courseId,
  courseSlug,
  moduleId,
  afterCreateRedirect,
  backHrefOverride,
  backLabelOverride,
  renderForm,
}: MaterialEditorPageProps) {
  const router = useRouter();
  const isEditMode = mode === "edit";
  const { createMaterial, isPending: isCreatePending } = useCreateMaterial();
  const { updateMaterial, isPending: isUpdatePending } = useUpdateMaterial();
  // Default ON when creating from a context (course/module/collection picker) — the
  // user's intent is "I want this material to be available now". For standalone
  // creation (no context) default OFF so authors can iterate before publishing.
  const isContextualCreate = !isEditMode && (!!courseId || !!afterCreateRedirect);
  const [publishOnCreate, setPublishOnCreate] = useState(isContextualCreate);
  const [notifySubscribers, setNotifySubscribers] = useState(true);

  const {
    data: material,
    isLoading,
    error,
  } = useQuery({
    ...materialDetailQueryOptions(materialId ?? ""),
    enabled: isEditMode && !!materialId,
  });

  const [isNavigating, setIsNavigating] = useState(false);
  const isPending = isCreatePending || isUpdatePending || isNavigating;

  const backHref =
    backHrefOverride ??
    (courseId ? routes.authorCourseBuilder(courseSlug ?? courseId!) : routes.authorKnowledgeBase);
  const backLabel = backLabelOverride ?? (courseId ? "← Назад к курсу" : "← Назад к базе знаний");

  const handleSubmit = async (
    values: Parameters<typeof createMaterial>[0] | Parameters<typeof updateMaterial>[0]["request"],
  ) => {
    if (isEditMode && materialId) {
      await updateMaterial({ materialId, request: values });
      return materialId;
    }

    // Атомарное создание: если есть courseId/moduleId — бэкенд в одной транзакции
    // создаёт материал + course_materials + module_items, и (если publishOnCreate)
    // переводит его в PUBLISHED. Один запрос вместо create+publish.
    const payload = {
      ...values,
      ...(courseId ? { courseId } : {}),
      ...(moduleId ? { moduleId } : {}),
      publishOnCreate,
      ...(publishOnCreate ? { notifySubscribers } : {}),
    } as Parameters<typeof createMaterial>[0];

    return await createMaterial(payload);
  };

  // В create-моде перенаправляемся на edit-страницу нового материала.
  // Контекст курса/модуля больше не таскаем через query — материал уже в course_materials
  // (создан атомарно в бэкенде), и edit-странице этот контекст не нужен.
  // setIsNavigating ставится ПЕРЕД router.replace, чтобы кнопка Save оставалась disabled
  // пока навигация в полёте (иначе моментальная re-enable + spam-клик).
  const handleAfterSubmit = async (newId: string | undefined) => {
    if (isEditMode) return;

    setIsNavigating(true);

    // Default: course-context create returns to course-builder (mirrors collection
    // flow); standalone create lands on the edit page so the author can iterate.
    const fallback = newId
      ? courseId
        ? routes.authorCourseBuilder(courseSlug ?? courseId)
        : routes.authorMaterialEdit(newId, { courseId, courseSlug })
      : courseId
        ? routes.authorCourseBuilder(courseSlug ?? courseId)
        : routes.authorKnowledgeBase;

    let target = fallback;
    if (newId && afterCreateRedirect) {
      try {
        const overridden = await afterCreateRedirect(newId);
        if (overridden) target = overridden;
      } catch {
        // Caller surfaces its own error toast (e.g. addItem failure). Fall back
        // to the material edit page so the new material isn't lost.
        target = routes.authorMaterialEdit(newId, { courseId, courseSlug });
      }
    }

    router.replace(target);
  };

  if (isEditMode && isLoading) {
    return (
      <div className="flex justify-center py-16">
        <Loader2 className="size-6 animate-spin text-muted-foreground" />
      </div>
    );
  }

  if (isEditMode && error) {
    return <ErrorCard error={error} className="py-16" />;
  }

  if (isEditMode && !material) {
    return (
      <NotFoundFallback
        message="Материал не найден"
        backHref={backHref}
        backLabel="К списку материалов"
      />
    );
  }

  const kindBadge = material ? getMaterialKindBadge(material.kind) : null;
  const statusBadge = material ? getMaterialStatusBadge(material.status) : null;

  return (
    <div className="min-h-full bg-background">
      <div className="border-b border-border/70 px-4 py-4 md:px-6">
        <div className="mx-auto flex w-full max-w-7xl flex-col gap-3 md:flex-row md:items-center md:justify-between md:gap-4">
          <div className="min-w-0 space-y-1">
            <Link
              href={backHref}
              className="inline-flex text-sm text-muted-foreground transition-colors hover:text-foreground"
            >
              {backLabel}
            </Link>
            <div className="flex flex-wrap items-center gap-2 md:gap-3">
              <h1 className="text-lg font-bold md:text-xl">
                {isEditMode
                  ? "Редактирование материала"
                  : courseId
                    ? "Новый материал для курса"
                    : "Новый материал"}
              </h1>
              {kindBadge && (
                <Badge variant="outline" className={kindBadge.className}>
                  {kindBadge.label}
                </Badge>
              )}
              {statusBadge && (
                <Badge variant="outline" className={statusBadge.className}>
                  {statusBadge.label}
                </Badge>
              )}
            </div>
          </div>

          <div className="flex w-full flex-wrap items-center gap-2 md:w-auto md:self-auto md:shrink-0">
            {isEditMode && material && (
              <MaterialAuthorActions material={material} context="editor" />
            )}
            {!isEditMode && (
              <>
                <div className="flex w-full flex-wrap items-center gap-x-3 gap-y-1.5 md:w-auto">
                  <label className="inline-flex min-h-[44px] select-none items-center gap-1.5 text-xs text-muted-foreground hover:text-foreground transition-colors cursor-pointer md:min-h-0">
                    <input
                      type="checkbox"
                      checked={publishOnCreate}
                      onChange={(e) => setPublishOnCreate(e.target.checked)}
                      disabled={isPending}
                      className="size-4 cursor-pointer accent-primary md:size-3.5"
                    />
                    Опубликовать сразу
                  </label>
                  {publishOnCreate && (
                    <label className="inline-flex min-h-[44px] select-none items-center gap-1.5 text-xs text-muted-foreground hover:text-foreground transition-colors cursor-pointer md:min-h-0">
                      <input
                        type="checkbox"
                        checked={notifySubscribers}
                        onChange={(e) => setNotifySubscribers(e.target.checked)}
                        disabled={isPending}
                        className="size-4 cursor-pointer accent-primary md:size-3.5"
                      />
                      Уведомить подписчиков
                    </label>
                  )}
                </div>
                <Button variant="ghost" className="min-touch flex-1 md:flex-none" asChild>
                  <Link href={backHref}>Отмена</Link>
                </Button>
              </>
            )}
            <Button
              type="submit"
              form={FORM_ID}
              disabled={isPending}
              className={
                isEditMode ? "min-touch flex-1 md:flex-none" : "min-touch flex-[2] md:flex-none"
              }
            >
              {isPending ? <Loader2 size={14} className="animate-spin" /> : <Save size={14} />}
              {isPending
                ? "Сохранение..."
                : isEditMode
                  ? "Сохранить"
                  : publishOnCreate
                    ? "Создать и опубликовать"
                    : "Создать"}
            </Button>
          </div>
        </div>
      </div>

      {renderForm({
        mode,
        materialId,
        material,
        formId: FORM_ID,
        defaultAccessType: getDefaultAccessType(courseId),
        onSubmit: handleSubmit,
        onAfterSubmit: handleAfterSubmit,
        isPending,
        onAutoSave:
          isEditMode && materialId
            ? async (values) => {
                await updateMaterial({ materialId, request: values });
              }
            : undefined,
      })}
    </div>
  );
}
