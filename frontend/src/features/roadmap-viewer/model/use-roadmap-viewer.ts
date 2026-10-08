import {
  buildProgressMap,
  computeOverallProgress,
  roadmapByCourseQueryOptions,
  roadmapQueryKeys,
  roadmapsApi,
  toReactFlowEdges,
  toReactFlowNodes,
  type RoadmapProgressItemRequest,
} from "@/entities/roadmap";
import { queryOptions, useQuery } from "@tanstack/react-query";

function roadmapProgressQueryOptions(items: RoadmapProgressItemRequest[]) {
  return queryOptions({
    queryKey: [roadmapQueryKeys.progress, items],
    queryFn: ({ signal }) => roadmapsApi.getRoadmapProgress(items, { signal }),
    select: (data) => data.result!,
    enabled: items.length > 0,
  });
}

export function useRoadmapViewer(courseId: string) {
  const roadmapQuery = useQuery(roadmapByCourseQueryOptions(courseId));
  const roadmap = roadmapQuery.data;

  const TRACKABLE_TYPES = new Set(["Material", "Issue", "Module", "Project"]);

  const progressItems: RoadmapProgressItemRequest[] = (roadmap?.nodes ?? [])
    .filter((n) => n.nodeType === "EntityReference")
    .map((n) => {
      const parsed = JSON.parse(n.data) as {
        entityType: string;
        entityId: string;
        courseId?: string;
      };
      return parsed;
    })
    .filter((p) => TRACKABLE_TYPES.has(p.entityType) && !!p.entityId)
    .map((p) => ({
      entityType: p.entityType,
      entityId: p.entityId,
      courseId: p.courseId ?? courseId,
    }));

  const progressQuery = useQuery(roadmapProgressQueryOptions(progressItems));

  const nodes = roadmap ? toReactFlowNodes(roadmap.nodes) : [];
  const edges = roadmap ? toReactFlowEdges(roadmap.edges) : [];

  const progressMap = buildProgressMap(progressQuery.data?.items ?? []);

  const enrolledCourseIds = new Set(
    progressQuery.data?.enrolledCourseIds ?? [],
  );

  const overallProgress = progressQuery.data
    ? computeOverallProgress(progressQuery.data.items)
    : null;

  return {
    roadmap,
    nodes,
    edges,
    progressMap,
    enrolledCourseIds,
    overallProgress,
    isLoading: roadmapQuery.isLoading,
    isProgressLoading: progressQuery.isLoading,
  };
}
