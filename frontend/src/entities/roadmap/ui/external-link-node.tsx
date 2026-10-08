"use client";

import { cn } from "@/shared/lib/css";
import { Handle, Position, type NodeProps } from "@xyflow/react";
import { ExternalLink } from "lucide-react";
import type { RoadmapReactFlowNodeData } from "../lib/transforms";
import { safeParseNodeData } from "../lib/node-types";
import type { ExternalLinkData } from "../types";

const hiddenHandle: React.CSSProperties = {
  width: 16,
  height: 16,
  background: "transparent",
  border: "none",
};

export function ExternalLinkNode({
  data,
  selected,
}: NodeProps & { data: RoadmapReactFlowNodeData }) {
  const parsed = safeParseNodeData<ExternalLinkData>(data.rawData, { url: "", title: "" });

  const readOnly = !!data.readOnly;

  return (
    <div
      className={cn(
        "w-[220px] rounded-xl border border-sky-500/20 bg-card/95 shadow-lg backdrop-blur-sm transition-all hover:border-sky-500/40",
        selected && "ring-2 ring-primary shadow-xl shadow-primary/10",
      )}
    >
      {!readOnly && (
        <>
          <Handle type="source" position={Position.Top} id="top" style={hiddenHandle} />
          <Handle type="source" position={Position.Bottom} id="bottom" style={hiddenHandle} />
          <Handle type="source" position={Position.Left} id="left" style={hiddenHandle} />
          <Handle type="source" position={Position.Right} id="right" style={hiddenHandle} />
        </>
      )}

      <div className="p-3">
        <div className="mb-2 flex items-center gap-2">
          <div className="flex size-7 items-center justify-center rounded-lg bg-sky-500">
            <ExternalLink className="size-4 text-white" />
          </div>
          <span className="text-2xs font-medium uppercase tracking-wider text-muted-foreground">
            Ссылка
          </span>
        </div>

        <p className="truncate text-sm font-bold leading-snug">
          {parsed.title || "Без названия"}
        </p>
        {parsed.description && (
          <p className="mt-1 line-clamp-2 text-xs text-muted-foreground">
            {parsed.description}
          </p>
        )}
        {parsed.url && (
          <p className="mt-1.5 truncate text-2xs text-sky-400/70">{parsed.url}</p>
        )}
      </div>
    </div>
  );
}
