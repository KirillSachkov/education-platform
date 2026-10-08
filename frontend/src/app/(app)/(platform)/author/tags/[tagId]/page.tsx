import { TagDetailPage } from "@/features/tag-management";

interface AuthorTagDetailPageProps {
  params: Promise<{ tagId: string }>;
}

export default async function AuthorTagDetailPage({
  params,
}: AuthorTagDetailPageProps) {
  const { tagId } = await params;
  return <TagDetailPage tagId={tagId} />;
}
