"use client";

import { use } from "react";
import { useSearchParams } from "next/navigation";
import { useQuery } from "@tanstack/react-query";
import { collectionDetailQueryOptions } from "@/entities/collection";
import { MaterialEditorPage } from "@/features/materials-manage";
import { MaterialForm, toMaterialFormInitialValue } from "@/features/material-edit";
import { MaterialQuizBindingSection } from "@/features/quiz-builder";
import { VideoUpload } from "@/features/video-upload";
import { routes } from "@/shared/config/routes";

interface Props {
  params: Promise<{ materialId: string }>;
}

export default function AuthorMaterialEditPage({ params }: Props) {
  const { materialId } = use(params);
  const searchParams = useSearchParams();
  const courseId = searchParams.get("courseId") ?? undefined;
  const courseSlug = searchParams.get("courseSlug") ?? undefined;
  const collectionId = searchParams.get("collectionId") ?? undefined;

  // Когда материал открыт из подборки (issue #216) — заменяем header-back и label на
  // подборку, чтобы автор мог вернуться одним кликом. Фолбэк на дефолты (курс или
  // база знаний) если query-param пустой или подборка не найдена.
  const { data: collection } = useQuery({
    ...collectionDetailQueryOptions(collectionId ?? ""),
    enabled: !!collectionId,
  });

  const backHrefOverride = collectionId
    ? routes.authorCollectionEdit(collectionId)
    : undefined;
  const backLabelOverride = collectionId
    ? collection?.title
      ? `← Назад к подборке «${collection.title}»`
      : "← Назад к подборке"
    : undefined;

  return (
    <MaterialEditorPage
      mode="edit"
      materialId={materialId}
      courseId={courseId}
      courseSlug={courseSlug}
      backHrefOverride={backHrefOverride}
      backLabelOverride={backLabelOverride}
      renderForm={(props) => (
        <MaterialForm
          mode={props.mode}
          context="full-page"
          materialId={props.materialId}
          material={props.material}
          formId={props.formId}
          initialValue={
            props.material
              ? toMaterialFormInitialValue(props.material)
              : undefined
          }
          onSubmit={props.onSubmit}
          onAfterSubmit={props.onAfterSubmit}
          hideSubmitButton
          isPending={props.isPending}
          renderVideoUpload={(vp) => <VideoUpload {...vp} />}
          // Блок привязки квиза (#494): standalone-квиз из библиотеки, привязка —
          // немедленный PATCH материала (quizId), без полного сабмита формы.
          renderQuizSection={(qp) => (
            <MaterialQuizBindingSection
              quizId={qp.quizId}
              onQuizIdChange={qp.onQuizIdChange}
              className="border-t border-border/60 pt-6"
            />
          )}
        />
      )}
    />
  );
}
