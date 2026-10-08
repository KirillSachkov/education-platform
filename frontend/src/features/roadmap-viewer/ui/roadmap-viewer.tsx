"use client";

import {
  DEFAULT_EDGE_OPTIONS,
  EntityReferenceNode,
  ExternalLinkNode,
  getEntityRoute,
  getProgressColor,
  GroupNode,
  PROGRESS_COLOR_MAP,
  TextNoteNode,
  type RoadmapReactFlowNodeData,
} from "@/entities/roadmap";
import { useCourseSlug } from "@/shared/providers/course-id-provider";
import { cn } from "@/shared/lib/css";
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
import { useRoadmapViewer } from "../model/use-roadmap-viewer";
import { NodeDetailCard, type NodeDetailData } from "./node-detail-card";

// Node types must be defined at module scope
const nodeTypes = {
  EntityReference: EntityReferenceNode,
  TextNote: TextNoteNode,
  ExternalLink: ExternalLinkNode,
  Group: GroupNode,
};

interface RoadmapViewerProps {
  courseId: string;
}

function RoadmapViewerInner({ courseId }: RoadmapViewerProps) {
  const courseSlug = useCourseSlug();
  const router = useRouter();
  const { roadmap, nodes, edges, progressMap, enrolledCourseIds, isLoading } =
    useRoadmapViewer(courseId);

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

    const targetRoute = getEntityRoute(courseSlug, parsed.entityType, parsed.entityId);
    if (!targetRoute) return;

    const color = getProgressColor(
      parsed.entityId,
      progressMap,
      enrolledCourseIds,
      parsed.courseId ?? courseId,
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
      route: targetRoute,
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
        <p className="text-sm">Роадмап пока не создан</p>
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
      parsed.courseId ?? courseId,
    );

    return {
      ...withReadOnly,
      className: cn(PROGRESS_COLOR_MAP[color], "rounded-lg"),
    };
  });

  return (
    <div className="relative flex h-full w-full min-w-0 flex-col overflow-hidden">
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

export function RoadmapViewer({ courseId }: RoadmapViewerProps) {
  return (
    <ReactFlowProvider>
      <RoadmapViewerInner courseId={courseId} />
    </ReactFlowProvider>
  );
}
