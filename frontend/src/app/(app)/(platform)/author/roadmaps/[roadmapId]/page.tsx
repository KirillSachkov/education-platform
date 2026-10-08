"use client";

import { roadmapQueryOptions } from "@/entities/roadmap";
import { useQuery } from "@tanstack/react-query";
import dynamic from "next/dynamic";
import { useParams } from "next/navigation";
import { Loader2, Map } from "lucide-react";

const RoadmapEditor = dynamic(
  () =>
    import("@/features/roadmap-editor").then((m) => ({
      default: m.RoadmapEditor,
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

export default function RoadmapEditorPage() {
  const params = useParams<{ roadmapId: string }>();
  const { data: roadmap, isLoading } = useQuery(
    roadmapQueryOptions(params.roadmapId),
  );

  if (isLoading) {
    return (
      <div className="flex h-[400px] items-center justify-center">
        <Loader2 className="size-6 animate-spin text-muted-foreground" />
      </div>
    );
  }

  if (!roadmap) {
    return (
      <div className="flex h-[calc(100svh-120px)] flex-col items-center justify-center gap-3 text-muted-foreground">
        <Map className="size-10" />
        <p className="text-sm">Роадмап не найден</p>
      </div>
    );
  }

  return (
    <div className="h-[calc(100svh-120px)]">
      <RoadmapEditor courseId={roadmap.courseId ?? ""} roadmapId={roadmap.id} />
    </div>
  );
}
