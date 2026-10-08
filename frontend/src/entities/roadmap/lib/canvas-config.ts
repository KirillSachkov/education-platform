import { MarkerType } from "@xyflow/react";

export const MINIMAP_NODE_COLOR: Record<string, string> = {
  EntityReference: "#3b82f6",
  TextNote: "#f59e0b",
  ExternalLink: "#0ea5e9",
  Group: "#8b5cf6",
};

export function getMinimapNodeColor(node: { data?: { nodeType?: string } }): string {
  return MINIMAP_NODE_COLOR[node.data?.nodeType ?? ""] ?? "#6b7280";
}

export const DEFAULT_EDGE_OPTIONS = {
  type: "default" as const,
  pathOptions: { curvature: 0.4 },
  style: { stroke: "#ffffff40", strokeWidth: 2, strokeDasharray: "6 4" },
  markerEnd: {
    type: MarkerType.ArrowClosed,
    color: "#ffffff60",
    width: 16,
    height: 16,
  },
  focusable: true,
  interactionWidth: 20,
};
