import type { Metadata } from "next";
import { APP_URL } from "@/shared/config/site";
import { ArticleJsonLd, buildEntityMetadata, fetchAnonymous } from "@/shared/seo";
import { SpaceCourseMaterialClient } from "./page-client";

interface Props {
  params: Promise<{ courseSlug: string; materialId: string }>;
}

interface MaterialPreview {
  title?: string;
  kind?: string;
  imageUrl?: string;
  publishedAt?: string | null;
  updatedAt?: string;
}

const KIND_LABELS: Record<string, string> = {
  ARTICLE: "Статья",
  VIDEO: "Видео",
  NOTE: "Заметка",
  STREAM: "Эфир",
};

export async function generateMetadata({ params }: Props): Promise<Metadata> {
  const { courseSlug, materialId } = await params;
  const material = await fetchAnonymous<MaterialPreview>(`/materials/${materialId}/preview/`);
  if (!material?.title) return { title: "Материал" };

  const kindLabel = material.kind ? (KIND_LABELS[material.kind] ?? "Материал") : "Материал";
  return buildEntityMetadata({
    title: material.title,
    description: `${kindLabel} на SachkovLearn`,
    imageUrl: material.imageUrl,
    path: `/courses/${courseSlug}/learn/${materialId}`,
    type: "article",
    fullTitle: `${material.title} — ${kindLabel.toLowerCase()} на SachkovLearn`,
  });
}

// Anon/not-enrolled users are allowed through — the client view shows a lock-preview
// with enroll/login CTA instead of a hard redirect to /login.
export default async function SpaceCourseMaterialPage({ params }: Props) {
  const { courseSlug, materialId } = await params;
  // Re-fetch of the same preview endpoint as generateMetadata — deduped by the
  // Next.js Data Cache (revalidate 300), so no extra backend request in practice.
  const material = await fetchAnonymous<MaterialPreview>(`/materials/${materialId}/preview/`);
  const kindLabel = material?.kind ? (KIND_LABELS[material.kind] ?? "Материал") : "Материал";
  return (
    <>
      {material?.title && (
        <ArticleJsonLd
          headline={material.title}
          description={`${kindLabel} на SachkovLearn`}
          image={material.imageUrl}
          datePublished={material.publishedAt}
          dateModified={material.updatedAt}
          publisherName="SachkovLearn"
          url={`${APP_URL}/courses/${courseSlug}/learn/${materialId}`}
        />
      )}
      <SpaceCourseMaterialClient materialId={materialId} />
    </>
  );
}
