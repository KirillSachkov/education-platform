import type { Metadata } from "next";
import { PublicRoadmapClient } from "./page-client";

const API_URL =
  process.env.API_URL_INTERNAL ?? process.env.NEXT_PUBLIC_API_URL ?? "http://localhost/api";

interface Props {
  params: Promise<{ slug: string }>;
}

export async function generateMetadata({ params }: Props): Promise<Metadata> {
  const { slug } = await params;
  try {
    const res = await fetch(`${API_URL}/roadmaps/by-slug/${slug}/`, {
      cache: "no-store",
    });
    if (!res.ok) return { title: "Roadmap" };
    const data = await res.json();
    const roadmap = data.result;
    return {
      title: roadmap?.title ?? "Roadmap",
      description: roadmap?.description ?? undefined,
    };
  } catch {
    return { title: "Roadmap" };
  }
}

export default async function PublicRoadmapPage({ params }: Props) {
  const { slug } = await params;
  return <PublicRoadmapClient slug={slug} />;
}
