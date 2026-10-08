"use client";

import { cn } from "@/shared/lib/css";
import { type NodeProps, NodeResizer } from "@xyflow/react";
import { Layers } from "lucide-react";
import type { RoadmapReactFlowNodeData } from "../lib/transforms";
import { safeParseNodeData } from "../lib/node-types";
import type { GroupData } from "../types";

export function GroupNode({
  data,
  selected,
}: NodeProps & { data: RoadmapReactFlowNodeData }) {
  const parsed = safeParseNodeData<GroupData>(data.rawData, { label: "" });

  const readOnly = !!data.readOnly;

  return (
    <div
      className={cn(
        "size-full min-h-[120px] min-w-[180px] rounded-2xl border-2 border-dashed border-violet-400/40 bg-violet-500/5 pointer-events-none",
        selected && "ring-2 ring-primary ring-offset-2 ring-offset-background",
      )}
    >
      {!readOnly && (
        <NodeResizer
          minWidth={180}
          minHeight={120}
          isVisible={selected ?? false}
          lineClassName="!border-primary !pointer-events-auto"
          handleClassName="!size-3 !rounded-sm !border-primary !bg-primary !pointer-events-auto"
        />
      )}

      <div className="pointer-events-auto flex items-center gap-2 px-3 pt-2.5 pb-1">
        <div className="flex items-center justify-center rounded-md bg-violet-500/10 p-1">
          <Layers className="size-3.5 text-violet-500" />
        </div>
        <span className="text-xs font-bold uppercase tracking-wider text-violet-400">
          {parsed.label}
        </span>
      </div>
    </div>
  );
}
