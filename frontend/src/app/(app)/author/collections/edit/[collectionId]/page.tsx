import { CollectionEditorPage } from "@/features/collection-manage";

interface Props {
  params: Promise<{ collectionId: string }>;
}

export default async function AuthorCollectionEditPage({ params }: Props) {
  const { collectionId } = await params;
  return <CollectionEditorPage collectionId={collectionId} />;
}
