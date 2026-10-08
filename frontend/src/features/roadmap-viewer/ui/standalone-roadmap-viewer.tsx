"use client";

import {
  buildProgressMap,
  DEFAULT_EDGE_OPTIONS,
  EntityReferenceNode,
  ExternalLinkNode,
  getEntityRoute,
  getProgressColor,
  GroupNode,
  PROGRESS_COLOR_MAP,
  roadmapBySlugQueryOptions,
  roadmapQueryKeys,
  roadmapsApi,
  TextNoteNode,
  toReactFlowEdges,
  toReactFlowNodes,
  type RoadmapProgressItemRequest,
  type RoadmapReactFlowNodeData,
} from "@/entities/roadmap";
import { useIsAuthenticated } from "@/shared/auth";
import { cn } from "@/shared/lib/css";
import { queryOptions, useQuery } from "@tanstack/react-query";
import {
  Background,
  Controls,
  ReactFlow,
  ReactFlowProvider,
  useReactFlow,
  type Node,
} from "@xyflow/react";
import "@xyflow/react/dist/style.css";
import { Loader2, Map } from "lucide-react";
import { useRouter } from "next/navigation";
import { useState } from "react";
import { NodeDetailCard, type NodeDetailData } from "./node-detail-card";

const nodeTypes = {
  EntityReference: EntityReferenceNode,
  TextNote: TextNoteNode,
  ExternalLink: ExternalLinkNode,
  Group: GroupNode,
};

function roadmapProgressQueryOptions(
  items: RoadmapProgressItemRequest[],
  isAuthenticated: boolean,
) {
  return queryOptions({
    queryKey: [roadmapQueryKeys.progress, "standalone", items],
    queryFn: ({ signal }) => roadmapsApi.getRoadmapProgress(items, { signal }),
    select: (data) => data.result!,
    enabled: items.length > 0 && isAuthenticated,
  });
}

interface StandaloneRoadmapViewerProps {
  slug: string;
}

function StandaloneViewerInner({ slug }: StandaloneRoadmapViewerProps) {
  const router = useRouter();
  const isAuthenticated = useIsAuthenticated();
  const { data: roadmap, isLoading } = useQuery(roadmapBySlugQueryOptions(slug));

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
      courseId: p.courseId,
    }));

  const progressQuery = useQuery(roadmapProgressQueryOptions(progressItems, isAuthenticated));

  const nodes = roadmap ? toReactFlowNodes(roadmap.nodes) : [];
  const edges = roadmap ? toReactFlowEdges(roadmap.edges) : [];

  const progressMap = buildProgressMap(progressQuery.data?.items ?? []);
  const enrolledCourseIds = new Set(progressQuery.data?.enrolledCourseIds ?? []);

  const { flowToScreenPosition } = useReactFlow();
  const [selectedDetail, setSelectedDetail] = useState<{
    data: NodeDetailData;
    position: { x: number; y: number };
    route: string;
  } | null>(null);

  function handleNodeClick(_: React.MouseEvent, node: Node<RoadmapReactFlowNodeData>) {
    if (node.type !== "EntityReference") {
      setSelectedDetail(null);
      return;
    }

    const parsed = JSON.parse(node.data.rawData) as {
      entityType: string;
      entityId: string;
      entityTitle?: string | null;
      entityDescription?: string | null;
      imageId?: string | null;
      courseId?: string;
    };

    if (!parsed.courseId) return;
    const href = getEntityRoute(parsed.courseId, parsed.entityType, parsed.entityId);
    if (!href) return;

    const color = getProgressColor(
      parsed.entityId,
      progressMap,
      enrolledCourseIds,
      parsed.courseId,
    );

    const screenPos = flowToScreenPosition({
      x: node.position.x + (node.measured?.width ?? node.width ?? 260),
      y: node.position.y,
    });

    setSelectedDetail({
      data: {
        entityType: parsed.entityType,
        entityId: parsed.entityId,
        entityTitle: parsed.entityTitle,
        entityDescription: parsed.entityDescription,
        imageId: parsed.imageId,
        progressColor: color,
      },
      position: screenPos,
      route: href,
    });
  }

  function handlePaneClick() {
    setSelectedDetail(null);
  }

  if (isLoading) {
    return (
      <div className="flex h-[400px] items-center justify-center">
        <Loader2 className="size-6 animate-spin text-muted-foreground" />
      </div>
    );
  }

  if (!roadmap) {
    return (
      <div className="flex h-[400px] flex-col items-center justify-center gap-3 text-muted-foreground">
        <Map className="size-10" />
        <p className="text-sm">Роадмап не найден</p>
      </div>
    );
  }

  const styledNodes = nodes.map((node) => {
    const withReadOnly = {
      ...node,
      data: { ...node.data, readOnly: true },
    };

    if (node.type !== "EntityReference") return withReadOnly;
    const parsed = JSON.parse((node.data as RoadmapReactFlowNodeData).rawData) as {
      entityId: string;
      courseId?: string;
    };
    const color = getProgressColor(
      parsed.entityId,
      progressMap,
      enrolledCourseIds,
      parsed.courseId,
    );
    return {
      ...withReadOnly,
      className: cn(PROGRESS_COLOR_MAP[color], "rounded-lg"),
    };
  });

  return (
    <div className="relative flex h-full w-full min-w-0 flex-col overflow-hidden">
      <div className="flex flex-col gap-1 border-b px-4 py-3 sm:flex-row sm:items-center sm:gap-3">
        <h1 className="text-lg font-semibold">{roadmap.title}</h1>
        {roadmap.description && (
          <p className="text-sm text-muted-foreground">{roadmap.description}</p>
        )}
      </div>

      <div className="min-h-0 w-full min-w-0 flex-1 overflow-hidden">
        <ReactFlow
          nodes={styledNodes}
          edges={edges}
          nodeTypes={nodeTypes}
          nodesDraggable={false}
          nodesConnectable={false}
          elementsSelectable={false}
          panOnScroll
          zoomOnScroll={false}
          zoomOnPinch
          panOnDrag
          minZoom={0.2}
          maxZoom={2}
          fitView
          onlyRenderVisibleElements
          defaultEdgeOptions={DEFAULT_EDGE_OPTIONS}
          onNodeClick={handleNodeClick}
          onPaneClick={handlePaneClick}
          className="dark:!bg-[hsl(var(--background))]"
        >
          <Background gap={20} size={1} className="!text-muted-foreground/20" />
          <Controls
            showInteractive={false}
            className="!rounded-lg !border !border-border !bg-card !shadow-sm [&>button]:!border-border [&>button]:!bg-card [&>button]:!text-foreground [&>button:hover]:!bg-accent"
          />
        </ReactFlow>
      </div>

      {selectedDetail && (
        <NodeDetailCard
          data={selectedDetail.data}
          position={selectedDetail.position}
          onNavigate={() => router.push(selectedDetail.route)}
          onClose={() => setSelectedDetail(null)}
        />
      )}
    </div>
  );
}

export function StandaloneRoadmapViewer({ slug }: StandaloneRoadmapViewerProps) {
  return (
    <ReactFlowProvider>
      <StandaloneViewerInner slug={slug} />
    </ReactFlowProvider>
  );
}
