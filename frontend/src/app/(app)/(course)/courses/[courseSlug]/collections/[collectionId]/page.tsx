import type { Metadata } from "next";
import { buildEntityMetadata, fetchAnonymous } from "@/shared/seo";
import { CourseCollectionDetailClient } from "./page-client";

interface Props {
  params: Promise<{
    courseSlug: string;
    collectionId: string;
  }>;
}

interface CollectionPreview {
  title?: string;
  description?: string;
  coverImageUrl?: string;
}

export async function generateMetadata({ params }: Props): Promise<Metadata> {
  const { courseSlug, collectionId } = await params;
  const collection = await fetchAnonymous<CollectionPreview>(
    `/collections/${collectionId}/detail/`,
  );
  if (!collection?.title) return { title: "Подборка" };

  return buildEntityMetadata({
    title: collection.title,
    description: collection.description ?? "Подборка материалов на SachkovLearn",
    imageUrl: collection.coverImageUrl,
    path: `/courses/${courseSlug}/collections/${collectionId}`,
    type: "article",
    fullTitle: `${collection.title} — подборка на SachkovLearn`,
  });
}

export default async function CourseCollectionDetailPage({ params }: Props) {
  const { collectionId } = await params;
  return <CourseCollectionDetailClient collectionId={collectionId} />;
}
