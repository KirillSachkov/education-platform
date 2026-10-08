import type { SaveCanvasRequest } from "@/entities/roadmap";
import { toSaveCanvasEdges, toSaveCanvasNodes } from "@/entities/roadmap";
import type { Edge, Node } from "@xyflow/react";
import { useEffect, useRef, useState } from "react";
import { useDebouncedCallback } from "use-debounce";
import { useSaveRoadmap } from "./use-save-roadmap";
import type { RoadmapReactFlowNodeData } from "@/entities/roadmap";

const AUTO_SAVE_DELAY_MS = 5000;

export function useAutoSave(roadmapId: string) {
  const saveMutation = useSaveRoadmap(roadmapId);
  const [isDirty, setIsDirty] = useState(false);
  const latestState = useRef<{
    nodes: Node<RoadmapReactFlowNodeData>[];
    edges: Edge[];
  }>({ nodes: [], edges: [] });

  const debouncedSave = useDebouncedCallback(() => {
    const { nodes, edges } = latestState.current;
    const request: SaveCanvasRequest = {
      nodes: toSaveCanvasNodes(nodes),
      edges: toSaveCanvasEdges(edges),
    };
    saveMutation.mutate(request, {
      onSuccess: () => setIsDirty(false),
    });
  }, AUTO_SAVE_DELAY_MS);

  // Flush pending save on unmount (SPA navigation doesn't trigger beforeunload).
  // debouncedSave intentionally excluded — we want ONE unmount-only cleanup,
  // not a cleanup that re-registers on every render.
  useEffect(() => {
    return () => {
      debouncedSave.flush();
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  function markDirty(
    nodes: Node<RoadmapReactFlowNodeData>[],
    edges: Edge[],
  ) {
    latestState.current = { nodes, edges };
    setIsDirty(true);
    debouncedSave();
  }

  function saveNow(
    nodes: Node<RoadmapReactFlowNodeData>[],
    edges: Edge[],
  ) {
    debouncedSave.cancel();
    latestState.current = { nodes, edges };
    const request: SaveCanvasRequest = {
      nodes: toSaveCanvasNodes(nodes),
      edges: toSaveCanvasEdges(edges),
    };
    saveMutation.mutate(request, {
      onSuccess: () => setIsDirty(false),
    });
  }

  return {
    isDirty,
    markDirty,
    saveNow,
    isSaving: saveMutation.isPending,
  };
}
