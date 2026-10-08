import type { Metadata } from "next";
import type { MaterialAccessType, MaterialDetailDto, MaterialKind } from "@/entities/material";
import { MaterialView } from "@/widgets/material-view";
import { routes } from "@/shared/config/routes";
import { APP_URL } from "@/shared/config/site";
import {
  ArticleJsonLd,
  BreadcrumbJsonLd,
  buildEntityMetadata,
  fetchAnonymous,
  stripMarkdown,
  truncate,
} from "@/shared/seo";

interface Props {
  params: Promise<{ materialId: string }>;
}

interface MaterialPreview {
  id: string;
  title: string;
  kind: MaterialKind;
  accessType: MaterialAccessType;
  imageUrl: string | null;
  publishedAt?: string | null;
  updatedAt?: string;
}

const KIND_LABELS: Record<string, string> = {
  ARTICLE: "Статья",
  VIDEO: "Видео",
  NOTE: "Заметка",
  STREAM: "Эфир",
};

interface PublicMaterialPageData {
  preview: MaterialPreview | null;
  detail: MaterialDetailDto | null;
}

async function fetchPublicMaterialPageData(materialId: string): Promise<PublicMaterialPageData> {
  const preview = await fetchAnonymous<MaterialPreview>(`/materials/${materialId}/preview/`);
  if (preview?.accessType !== "PUBLIC") return { preview, detail: null };

  const detail = await fetchAnonymous<MaterialDetailDto>(`/materials/${materialId}/detail/`, {
    cache: "no-store",
  });
  // Fail closed if the two endpoint snapshots disagree during a concurrent
  // access-type change. A restricted body must never become SSR initial data.
  return { preview, detail: detail?.accessType === "PUBLIC" ? detail : null };
}

function buildMaterialDescription(
  preview: MaterialPreview,
  detail: MaterialDetailDto | null,
): string {
  const publicText = detail?.description?.trim() || detail?.content?.trim();
  if (publicText) {
    const safeText = stripMarkdown(publicText);
    if (safeText) return truncate(safeText, 160);
  }

  const kindLabel = KIND_LABELS[preview.kind] ?? "Материал";
  return preview.accessType === "REGISTERED"
    ? `${kindLabel} «${preview.title}» на SachkovLearn. Войдите, чтобы открыть материал.`
    : preview.accessType === "ENROLLED"
      ? `${kindLabel} «${preview.title}» на SachkovLearn. Получите доступ к курсу, чтобы открыть материал.`
      : `${kindLabel} «${preview.title}» на SachkovLearn.`;
}

export async function generateMetadata({ params }: Props): Promise<Metadata> {
  const { materialId } = await params;
  const { preview, detail } = await fetchPublicMaterialPageData(materialId);
  if (!preview?.title) return { title: "Материал" };

  const kindLabel = KIND_LABELS[preview.kind] ?? "Материал";
  return buildEntityMetadata({
    title: preview.title,
    description: buildMaterialDescription(preview, detail),
    imageUrl: preview.imageUrl,
    path: `/knowledge-base/${materialId}`,
    type: "article",
    fullTitle: `${preview.title} — ${kindLabel.toLowerCase()} на SachkovLearn`,
  });
}

export default async function SpaceMaterialDetailPage({ params }: Props) {
  const { materialId } = await params;
  // Preview metadata is cached briefly; PUBLIC detail deliberately bypasses
  // that cache so an access-type change fails closed immediately.
  const { preview, detail } = await fetchPublicMaterialPageData(materialId);
  const url = `${APP_URL}${routes.knowledgeBaseMaterial(materialId)}`;
  const description = preview ? buildMaterialDescription(preview, detail) : null;
  return (
    <>
      {preview?.title && (
        <>
          <ArticleJsonLd
            headline={preview.title}
            description={description}
            image={preview.imageUrl}
            datePublished={preview.publishedAt}
            dateModified={preview.updatedAt}
            authorName={detail?.authorDisplayName}
            publisherName="SachkovLearn"
            url={url}
          />
          <BreadcrumbJsonLd
            items={[
              { name: "Главная", url: APP_URL },
              { name: "База знаний", url: `${APP_URL}${routes.knowledgeBase}` },
              { name: preview.title, url },
            ]}
          />
        </>
      )}
      <MaterialView
        materialId={materialId}
        mode="learning"
        backHref={routes.knowledgeBase}
        initialMaterial={detail ?? undefined}
      />
    </>
  );
}
