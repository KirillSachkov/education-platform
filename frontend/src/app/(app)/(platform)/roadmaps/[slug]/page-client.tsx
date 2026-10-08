"use client";

import dynamic from "next/dynamic";
import { Loader2 } from "lucide-react";

const StandaloneRoadmapViewer = dynamic(
  () =>
    import("@/features/roadmap-viewer").then((m) => ({
      default: m.StandaloneRoadmapViewer,
    })),
  {
    ssr: false,
    loading: () => (
      <div className="flex h-[400px] items-center justify-center">
        <Loader2 className="size-6 animate-spin text-muted-foreground" />
      </div>
    ),
  },
);

export function PublicRoadmapClient({ slug }: { slug: string }) {
  return (
    <div className="h-[calc(100svh-120px)]">
      <StandaloneRoadmapViewer slug={slug} />
    </div>
  );
}
