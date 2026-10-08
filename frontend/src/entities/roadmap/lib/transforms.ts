import type { Edge, Node } from "@xyflow/react";
import type {
  RoadmapEdgeDto,
  RoadmapNodeDto,
  SaveCanvasEdgeDto,
  SaveCanvasNodeDto,
} from "../types";

export interface RoadmapReactFlowNodeData extends Record<string, unknown> {
  nodeType: string;
  rawData: string;
  sortOrder: number;
}

export function toReactFlowNodes(
  dtoNodes: RoadmapNodeDto[],
): Node<RoadmapReactFlowNodeData>[] {
  return dtoNodes.map((n) => ({
    id: n.id,
    type: n.nodeType,
    position: { x: n.positionX, y: n.positionY },
    data: {
      nodeType: n.nodeType,
      rawData: n.data,
      sortOrder: n.sortOrder,
    },
    parentId: n.parentNodeId ?? undefined,
    zIndex: n.nodeType === "Group" ? -1 : 0,
    ...(n.width != null && n.height != null
      ? { width: n.width, height: n.height }
      : {}),
  }));
}

export function toReactFlowEdges(dtoEdges: RoadmapEdgeDto[]): Edge[] {
  return dtoEdges.map((e) => ({
    id: e.id,
    source: e.sourceNodeId,
    target: e.targetNodeId,
    type: e.edgeType || "smoothstep",
    label: e.label ?? undefined,
    animated: e.animated,
    sourceHandle: e.sourceHandle ?? undefined,
    targetHandle: e.targetHandle ?? undefined,
  }));
}

export function toSaveCanvasNodes(
  nodes: Node<RoadmapReactFlowNodeData>[],
): SaveCanvasNodeDto[] {
  return nodes.map((n, i) => ({
    id: n.id,
    nodeType: n.data.nodeType,
    positionX: n.position.x,
    positionY: n.position.y,
    width: n.measured?.width ?? n.width ?? null,
    height: n.measured?.height ?? n.height ?? null,
    parentNodeId: n.parentId ?? null,
    data: n.data.rawData,
    sortOrder: n.data.sortOrder ?? i,
  }));
}

export function toSaveCanvasEdges(edges: Edge[]): SaveCanvasEdgeDto[] {
  return edges.map((e) => ({
    id: e.id,
    sourceNodeId: e.source,
    targetNodeId: e.target,
    label: (e.label as string) ?? null,
    edgeType: e.type ?? "smoothstep",
    animated: e.animated ?? false,
    sourceHandle: e.sourceHandle ?? null,
    targetHandle: e.targetHandle ?? null,
  }));
}
