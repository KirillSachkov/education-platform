"use client";

import {
  EntityReferenceNode,
  TextNoteNode,
  ExternalLinkNode,
  GroupNode,
  roadmapQueryOptions,
  toReactFlowEdges,
  toReactFlowNodes,
  DEFAULT_EDGE_OPTIONS,
  type RoadmapReactFlowNodeData,
  type EntityReferenceData,
} from "@/entities/roadmap";
import {
  addEdge,
  Background,
  Controls,
  ReactFlow,
  ReactFlowProvider,
  useEdgesState,
  useNodesState,
  useReactFlow,
  type Connection,
  type Edge,
  type Node,
  type NodeChange,
  type EdgeChange,
} from "@xyflow/react";
import "@xyflow/react/dist/style.css";
import { Loader2, Map } from "lucide-react";
import { useEffect, useRef, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { useAutoSave } from "../model/use-auto-save";
import { useUndoRedo } from "../model/use-undo-redo";
import { EditorToolbar } from "./editor-toolbar";
import { NodePropertiesPanel } from "./node-properties-panel";
import { EntityPickerDialog } from "./entity-picker-dialog";

// Module-scope — prevents ReactFlow re-registration
const nodeTypes = {
  EntityReference: EntityReferenceNode,
  TextNote: TextNoteNode,
  ExternalLink: ExternalLinkNode,
  Group: GroupNode,
};

interface RoadmapEditorProps {
  courseId: string;
  roadmapId: string;
}

function RoadmapEditorInner({ roadmapId }: RoadmapEditorProps) {
  const { data: roadmap, isLoading } = useQuery(roadmapQueryOptions(roadmapId));
  const reactFlowInstance = useReactFlow();

  const [nodes, setNodes, onNodesChange] = useNodesState<Node<RoadmapReactFlowNodeData>>([]);
  const [edges, setEdges, onEdgesChange] = useEdgesState<Edge>([]);
  const [selectedNodeId, setSelectedNodeId] = useState<string | null>(null);
  const [entityPickerOpen, setEntityPickerOpen] = useState(false);

  const initializedRef = useRef(false);
  const { isDirty, markDirty, saveNow, isSaving } = useAutoSave(roadmapId);
  const { pushState, undo, redo, canUndo, canRedo } = useUndoRedo();

  const selectedNode = selectedNodeId ? (nodes.find((n) => n.id === selectedNodeId) ?? null) : null;

  // Load server data into local state on first fetch
  useEffect(() => {
    if (roadmap && !initializedRef.current) {
      const rfNodes = toReactFlowNodes(roadmap.nodes);
      const rfEdges = toReactFlowEdges(roadmap.edges);
      setNodes(rfNodes);
      setEdges(rfEdges);
      pushState({ nodes: rfNodes, edges: rfEdges });
      initializedRef.current = true;
    }
  }, [roadmap, setNodes, setEdges, pushState]);

  // Warn on unsaved changes
  useEffect(() => {
    function handleBeforeUnload(e: BeforeUnloadEvent) {
      if (isDirty) e.preventDefault();
    }
    window.addEventListener("beforeunload", handleBeforeUnload);
    return () => window.removeEventListener("beforeunload", handleBeforeUnload);
  }, [isDirty]);

  function handleNodesChange(changes: NodeChange<Node<RoadmapReactFlowNodeData>>[]) {
    onNodesChange(changes);

    // Track selection
    const selectionChanges = changes.filter((c) => c.type === "select");
    if (selectionChanges.length > 0) {
      const selectedNodes = selectionChanges.filter((c) => "selected" in c && c.selected);
      setSelectedNodeId(selectedNodes.length === 1 ? selectedNodes[0].id : null);
    }

    const hasDragStop = changes.some((c) => c.type === "position" && !c.dragging);
    const hasRemove = changes.some((c) => c.type === "remove");
    const hasResize = changes.some((c) => c.type === "dimensions");

    if (hasRemove) {
      setSelectedNodeId(null);
    }

    if (hasDragStop || hasRemove || hasResize) {
      const updatedNodes = reactFlowInstance.getNodes() as Node<RoadmapReactFlowNodeData>[];
      const currentEdges = reactFlowInstance.getEdges();
      pushState({ nodes: updatedNodes, edges: currentEdges });
      markDirty(updatedNodes, currentEdges);
    }
  }

  function handleEdgesChange(changes: EdgeChange<Edge>[]) {
    onEdgesChange(changes);

    const hasRemove = changes.some((c) => c.type === "remove");
    if (hasRemove) {
      const currentNodes = reactFlowInstance.getNodes() as Node<RoadmapReactFlowNodeData>[];
      const updatedEdges = reactFlowInstance.getEdges();
      pushState({ nodes: currentNodes, edges: updatedEdges });
      markDirty(currentNodes, updatedEdges);
    }
  }

  function handleConnect(connection: Connection) {
    setEdges((eds) => {
      const updated = addEdge({ ...connection, id: crypto.randomUUID(), type: "smoothstep" }, eds);
      const currentNodes = reactFlowInstance.getNodes() as Node<RoadmapReactFlowNodeData>[];
      pushState({ nodes: currentNodes, edges: updated });
      markDirty(currentNodes, updated);
      return updated;
    });
  }

  function handleAddNode(type: string) {
    const viewport = reactFlowInstance.getViewport();
    const centerX = (-viewport.x + window.innerWidth / 2) / viewport.zoom;
    const centerY = (-viewport.y + window.innerHeight / 2) / viewport.zoom;

    const defaultData: Record<string, string> = {
      EntityReference: JSON.stringify({
        entityType: "Material",
        entityId: "",
        entityTitle: "Новый элемент",
      }),
      TextNote: JSON.stringify({ text: "Заголовок" }),
      Group: JSON.stringify({ label: "Группа" }),
    };

    const newNode: Node<RoadmapReactFlowNodeData> = {
      id: crypto.randomUUID(),
      type,
      position: { x: centerX, y: centerY },
      data: {
        nodeType: type,
        rawData: defaultData[type] ?? "{}",
        sortOrder: nodes.length,
      },
      ...(type === "Group" ? { width: 300, height: 200, zIndex: -1 } : {}),
    };

    const currentEdges = reactFlowInstance.getEdges();

    setNodes((nds) => {
      const updated = [...nds, newNode];
      pushState({ nodes: updated, edges: currentEdges });
      markDirty(updated, currentEdges);
      return updated;
    });

    setSelectedNodeId(newNode.id);
  }

  function handleUpdateNodeData(nodeId: string, newRawData: string) {
    const currentEdges = reactFlowInstance.getEdges();
    setNodes((nds) => {
      const updated = nds.map((n) =>
        n.id === nodeId ? { ...n, data: { ...n.data, rawData: newRawData } } : n,
      );
      pushState({ nodes: updated, edges: currentEdges });
      markDirty(updated, currentEdges);
      return updated;
    });
  }

  function handleDeleteNode(nodeId: string) {
    const currentEdges = reactFlowInstance.getEdges();
    setNodes((nds) => {
      const updated = nds.filter((n) => n.id !== nodeId);
      const updatedEdges = currentEdges.filter((e) => e.source !== nodeId && e.target !== nodeId);
      setEdges(updatedEdges);
      pushState({ nodes: updated, edges: updatedEdges });
      markDirty(updated, updatedEdges);
      setSelectedNodeId(null);
      return updated;
    });
  }

  // Inline text editing events from TextNoteNode.
  // Uses CustomEvents on window intentionally: ReactFlow node data must remain
  // serializable for undo/redo, so mutable callbacks can't be passed through
  // node data. The effect has no dependency array to always bind fresh closures
  // (handleDeleteNode, markDirty, etc.) without stale captures.
  useEffect(() => {
    function onTextChange(e: Event) {
      const detail = (e as CustomEvent).detail;
      setNodes((nds) => {
        const updated = nds.map((n) =>
          n.id === detail.nodeId
            ? {
                ...n,
                data: {
                  ...n.data,
                  rawData: JSON.stringify({ text: detail.text, fontSize: detail.fontSize }),
                },
              }
            : n,
        );
        markDirty(updated, reactFlowInstance.getEdges());
        return updated;
      });
    }
    function onDeleteEmpty(e: Event) {
      handleDeleteNode((e as CustomEvent).detail.nodeId);
    }
    function onLockDrag(e: Event) {
      const { nodeId, locked } = (e as CustomEvent).detail;
      setNodes((nds) => nds.map((n) => (n.id === nodeId ? { ...n, draggable: !locked } : n)));
    }
    window.addEventListener("roadmap-node-text-change", onTextChange);
    window.addEventListener("roadmap-node-delete-empty", onDeleteEmpty);
    window.addEventListener("roadmap-node-lock-drag", onLockDrag);
    return () => {
      window.removeEventListener("roadmap-node-text-change", onTextChange);
      window.removeEventListener("roadmap-node-delete-empty", onDeleteEmpty);
      window.removeEventListener("roadmap-node-lock-drag", onLockDrag);
    };
  });

  function handleEntitySelected(entityData: EntityReferenceData) {
    if (!selectedNodeId) return;
    const newRawData = JSON.stringify(entityData);
    handleUpdateNodeData(selectedNodeId, newRawData);
  }

  function handlePaneClick() {
    setSelectedNodeId(null);
  }

  function handleUndo() {
    const previous = undo({ nodes, edges });
    if (previous) {
      setNodes(previous.nodes);
      setEdges(previous.edges);
      markDirty(previous.nodes, previous.edges);
      setSelectedNodeId(null);
    }
  }

  function handleRedo() {
    const next = redo({ nodes, edges });
    if (next) {
      setNodes(next.nodes);
      setEdges(next.edges);
      markDirty(next.nodes, next.edges);
      setSelectedNodeId(null);
    }
  }

  // Keyboard shortcuts
  useEffect(() => {
    function handleKeyDown(e: KeyboardEvent) {
      const tag = (e.target as HTMLElement)?.tagName;
      if (tag === "INPUT" || tag === "TEXTAREA" || tag === "SELECT") return;

      if ((e.metaKey || e.ctrlKey) && e.key === "z" && !e.shiftKey) {
        e.preventDefault();
        handleUndo();
      }
      if ((e.metaKey || e.ctrlKey) && e.key === "z" && e.shiftKey) {
        e.preventDefault();
        handleRedo();
      }
      if ((e.metaKey || e.ctrlKey) && e.key === "s") {
        e.preventDefault();
        const n = reactFlowInstance.getNodes() as Node<RoadmapReactFlowNodeData>[];
        const e2 = reactFlowInstance.getEdges();
        saveNow(n, e2);
      }
      // Number keys to add nodes
      if (e.key === "1") handleAddNode("EntityReference");
      if (e.key === "2") handleAddNode("TextNote");
      if (e.key === "3") handleAddNode("Group");
    }
    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
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
      <div className="flex h-[400px] flex-col items-center justify-center gap-3 text-muted-foreground">
        <Map className="size-10" />
        <p className="text-sm">Роадмап не найден</p>
      </div>
    );
  }

  return (
    <div className="flex h-full w-full min-w-0 flex-col overflow-hidden">
      {/* Edge selection + hover styles */}
      <style>{`
        .react-flow__edge.selected path { stroke: #22c55e !important; stroke-width: 3 !important; stroke-dasharray: none !important; }
        .react-flow__edge path:hover { stroke: #ffffff80 !important; stroke-width: 3 !important; cursor: pointer; }
      `}</style>
      <EditorToolbar
        isDirty={isDirty}
        isSaving={isSaving}
        canUndo={canUndo}
        canRedo={canRedo}
        nodeCount={nodes.length}
        onSave={() => saveNow(nodes, edges)}
        onUndo={handleUndo}
        onRedo={handleRedo}
        onAddNode={handleAddNode}
        onFitView={() => reactFlowInstance.fitView({ padding: 0.2, duration: 300 })}
      />

      <div className="relative min-h-0 w-full min-w-0 flex-1 overflow-hidden">
        <ReactFlow
          nodes={nodes}
          edges={edges}
          nodeTypes={nodeTypes}
          onNodesChange={handleNodesChange}
          onEdgesChange={handleEdgesChange}
          onConnect={handleConnect}
          onNodeClick={(_: React.MouseEvent, node: Node) => setSelectedNodeId(node.id)}
          onPaneClick={handlePaneClick}
          connectionMode={"loose" as never}
          fitView
          minZoom={0.1}
          maxZoom={3}
          panOnScroll
          zoomOnScroll={false}
          zoomOnPinch
          defaultEdgeOptions={DEFAULT_EDGE_OPTIONS}
          deleteKeyCode={["Backspace", "Delete"]}
          multiSelectionKeyCode="Shift"
          selectionKeyCode="Shift"
          selectionMode={"partial" as never}
          className="dark:!bg-[hsl(var(--background))]"
        >
          <Background gap={24} size={1.5} className="!text-muted-foreground/10" />
          <Controls className="!rounded-lg !border !border-border !bg-card !shadow-sm [&>button]:!border-border [&>button]:!bg-card [&>button]:!text-foreground [&>button:hover]:!bg-accent" />
        </ReactFlow>

        {selectedNode && (
          <NodePropertiesPanel
            node={selectedNode}
            onUpdateNodeData={handleUpdateNodeData}
            onDeleteNode={handleDeleteNode}
            onClose={() => setSelectedNodeId(null)}
            onOpenEntityPicker={() => setEntityPickerOpen(true)}
          />
        )}
      </div>

      <EntityPickerDialog
        open={entityPickerOpen}
        onOpenChange={setEntityPickerOpen}
        onSelect={handleEntitySelected}
      />
    </div>
  );
}

export function RoadmapEditor({ courseId, roadmapId }: RoadmapEditorProps) {
  return (
    <ReactFlowProvider>
      <RoadmapEditorInner courseId={courseId} roadmapId={roadmapId} />
    </ReactFlowProvider>
  );
}
