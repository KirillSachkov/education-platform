"use client";

import { roadmapByCourseQueryOptions, roadmapQueryKeys, roadmapsApi } from "@/entities/roadmap";
import { getErrorMessage } from "@/shared/api/errors";
import { useCourseId } from "@/shared/providers/course-id-provider";
import { Button } from "@/shared/ui/kit/button";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import dynamic from "next/dynamic";
import { Loader2, Map, Plus } from "lucide-react";
import { toast } from "sonner";

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

export default function AuthorRoadmapPage() {
  const courseId = useCourseId();
  const queryClient = useQueryClient();

  const { data: roadmap, isLoading } = useQuery(roadmapByCourseQueryOptions(courseId));

  const createMutation = useMutation({
    mutationFn: () =>
      roadmapsApi.createRoadmap({
        title: "Роадмап курса",
        courseId,
      }),
    onSuccess: () => {
      toast.success("Роадмап создан");
      queryClient.invalidateQueries({
        queryKey: [roadmapQueryKeys.base, "by-course", courseId],
      });
    },
    onError: (error) => toast.error(getErrorMessage(error, "Ошибка создания роадмапа")),
  });

  if (isLoading) {
    return (
      <div className="flex h-[400px] items-center justify-center">
        <Loader2 className="size-6 animate-spin text-muted-foreground" />
      </div>
    );
  }

  if (!roadmap) {
    return (
      <div className="flex h-[calc(100svh-120px)] flex-col items-center justify-center gap-4">
        <Map className="size-12 text-muted-foreground" />
        <p className="text-muted-foreground">У этого курса пока нет роадмапа</p>
        <Button
          onClick={() => createMutation.mutate()}
          disabled={createMutation.isPending}
          className="gap-2"
        >
          {createMutation.isPending ? (
            <Loader2 className="size-4 animate-spin" />
          ) : (
            <Plus className="size-4" />
          )}
          Создать роадмап
        </Button>
      </div>
    );
  }

  return (
    <div className="h-[calc(100svh-120px)]">
      <RoadmapEditor courseId={courseId} roadmapId={roadmap.id} />
    </div>
  );
}
