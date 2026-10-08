import { permanentRedirect } from "next/navigation";

interface SpacePricingDetailPageProps {
  params: Promise<{ slug: string }>;
}

export default async function SpacePricingDetailPage({ params }: SpacePricingDetailPageProps) {
  const { slug } = await params;
  permanentRedirect(`/pricing#${encodeURIComponent(slug)}`);
}
