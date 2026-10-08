import type { Edge, Node } from "@xyflow/react";
import { useRef, useState } from "react";
import type { RoadmapReactFlowNodeData } from "@/entities/roadmap";

interface HistoryEntry {
  nodes: Node<RoadmapReactFlowNodeData>[];
  edges: Edge[];
}

const MAX_HISTORY = 30;

export function useUndoRedo() {
  const past = useRef<HistoryEntry[]>([]);
  const future = useRef<HistoryEntry[]>([]);
  const [canUndo, setCanUndo] = useState(false);
  const [canRedo, setCanRedo] = useState(false);

  function pushState(entry: HistoryEntry) {
    past.current = [...past.current.slice(-(MAX_HISTORY - 1)), entry];
    future.current = [];
    setCanUndo(true);
    setCanRedo(false);
  }

  function undo(
    currentEntry: HistoryEntry,
  ): HistoryEntry | null {
    if (past.current.length === 0) return null;

    const previous = past.current[past.current.length - 1];
    past.current = past.current.slice(0, -1);
    future.current = [...future.current, currentEntry];

    setCanUndo(past.current.length > 0);
    setCanRedo(true);

    return previous;
  }

  function redo(
    currentEntry: HistoryEntry,
  ): HistoryEntry | null {
    if (future.current.length === 0) return null;

    const next = future.current[future.current.length - 1];
    future.current = future.current.slice(0, -1);
    past.current = [...past.current, currentEntry];

    setCanUndo(true);
    setCanRedo(future.current.length > 0);

    return next;
  }

  return { pushState, undo, redo, canUndo, canRedo };
}
