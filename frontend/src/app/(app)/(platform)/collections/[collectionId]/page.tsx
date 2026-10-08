import type { Metadata } from "next";
import { buildEntityMetadata, fetchAnonymous } from "@/shared/seo";
import { SpaceCollectionDetailClient } from "./page-client";

interface Props {
  params: Promise<{ collectionId: string }>;
}

interface CollectionPreview {
  title?: string;
  description?: string;
  coverImageUrl?: string;
}

export async function generateMetadata({ params }: Props): Promise<Metadata> {
  const { collectionId } = await params;
  const collection = await fetchAnonymous<CollectionPreview>(`/collections/${collectionId}/detail/`);
  if (!collection?.title) return { title: "Подборка" };

  return buildEntityMetadata({
    title: collection.title,
    description: collection.description ?? "Подборка материалов на SachkovLearn",
    imageUrl: collection.coverImageUrl,
    path: `/collections/${collectionId}`,
    type: "article",
    fullTitle: `${collection.title} — подборка на SachkovLearn`,
  });
}

export default async function SpaceCollectionDetailPage({ params }: Props) {
  const { collectionId } = await params;
  return <SpaceCollectionDetailClient collectionId={collectionId} />;
}
