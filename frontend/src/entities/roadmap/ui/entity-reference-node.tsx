"use client";

import { cn } from "@/shared/lib/css";
import { Handle, Position, type NodeProps } from "@xyflow/react";
import {
  HelpCircle,
  LayoutList,
} from "lucide-react";
import {
  ENTITY_ICONS,
  ENTITY_BG_COLORS,
  ENTITY_BORDER_COLORS,
  ENTITY_LABELS,
} from "@/shared/config/entity-icons";
import { ContentImage } from "@/shared/ui/components";
import { useState } from "react";
import type { RoadmapReactFlowNodeData } from "../lib/transforms";
import { safeParseNodeData } from "../lib/node-types";
import type { EntityReferenceData } from "../types";

/** Static icon lookup by PascalCase entity type — avoids dynamic component creation during render */
const NODE_ICONS: Record<string, typeof HelpCircle> = {
  Course: ENTITY_ICONS.course,
  Module: ENTITY_ICONS.module,
  Material: ENTITY_ICONS.lesson,
  Project: ENTITY_ICONS.project,
  Issue: ENTITY_ICONS.issue,
  Quiz: HelpCircle,
};

const NODE_BG: Record<string, string> = {
  Course: ENTITY_BG_COLORS.course,
  Module: ENTITY_BG_COLORS.module,
  Material: ENTITY_BG_COLORS.lesson,
  Project: ENTITY_BG_COLORS.project,
  Issue: ENTITY_BG_COLORS.issue,
  Quiz: "bg-amber-500",
};

const NODE_BORDER: Record<string, string> = {
  Course: ENTITY_BORDER_COLORS.course,
  Module: ENTITY_BORDER_COLORS.module,
  Material: ENTITY_BORDER_COLORS.lesson,
  Project: ENTITY_BORDER_COLORS.project,
  Issue: ENTITY_BORDER_COLORS.issue,
  Quiz: "border-amber-500/20 hover:border-amber-500/40",
};

const NODE_LABEL: Record<string, string> = {
  Course: ENTITY_LABELS.course,
  Module: ENTITY_LABELS.module,
  Material: "Материал",
  Project: ENTITY_LABELS.project,
  Issue: ENTITY_LABELS.issue,
  Quiz: "Тест",
};

// Handles: invisible by default, appear on node hover via CSS
const handleClass = "!w-3 !h-3 !bg-primary/0 !border-2 !border-transparent !rounded-full !transition-all group-hover:!bg-primary group-hover:!border-primary/50";

export function EntityReferenceNode({
  data,
  selected,
}: NodeProps & { data: RoadmapReactFlowNodeData }) {
  const parsed = safeParseNodeData<EntityReferenceData>(data.rawData, {
    entityType: "Material",
    entityId: "",
  });
  const Icon = NODE_ICONS[parsed.entityType] ?? LayoutList;
  const iconBg = NODE_BG[parsed.entityType] ?? "bg-muted";
  const borderClass = NODE_BORDER[parsed.entityType] ?? "border-border";
  const typeLabel = NODE_LABEL[parsed.entityType] ?? parsed.entityType;
  const isLinked = !!parsed.entityId;
  const [imgError, setImgError] = useState(false);
  const imageUrl =
    parsed.imageId && !imgError
      ? `/api/files/${parsed.imageId}/content`
      : null;

  const readOnly = !!data.readOnly;

  return (
    <div
      className={cn(
        "group min-w-[200px] w-[260px] overflow-hidden rounded-xl border bg-card/95 shadow-lg backdrop-blur-sm transition-all",
        borderClass,
        !isLinked && "border-dashed opacity-60",
        selected && "ring-2 ring-primary shadow-xl shadow-primary/10",
        readOnly && "cursor-pointer hover:shadow-xl hover:scale-[1.02] hover:brightness-110",
      )}
    >
      {!readOnly && (
        <>
          <Handle type="source" position={Position.Top} id="top" className={handleClass} />
          <Handle type="source" position={Position.Bottom} id="bottom" className={handleClass} />
          <Handle type="source" position={Position.Left} id="left" className={handleClass} />
          <Handle type="source" position={Position.Right} id="right" className={handleClass} />
        </>
      )}


      {/* Cover image */}
      {imageUrl ? (
        <div className="relative h-28 w-full overflow-hidden">
          <ContentImage
            src={imageUrl}
            alt={parsed.entityTitle ?? ""}
            fill
            sizes="260px"
            className="object-cover"
            onError={() => setImgError(true)}
          />
          <div className="absolute inset-0 bg-gradient-to-t from-card/80 to-transparent" />
        </div>
      ) : isLinked ? (
        <div className="h-12 w-full bg-gradient-to-br from-primary/10 via-primary/5 to-transparent" />
      ) : null}

      <div className="p-3">
        <div className="mb-2 flex items-center gap-2">
          <div className={cn("flex size-7 items-center justify-center rounded-lg", iconBg)}>
            <Icon className="size-4 text-white" />
          </div>
          <span className="text-2xs font-medium uppercase tracking-wider text-muted-foreground">
            {typeLabel}
          </span>
        </div>

        <p className="text-sm font-bold leading-snug">
          {parsed.entityTitle || "Не привязан"}
        </p>

        {parsed.entityDescription && (
          <p className="mt-1.5 line-clamp-3 text-xs leading-relaxed text-muted-foreground">
            {parsed.entityDescription}
          </p>
        )}

        {!isLinked && (
          <p className="mt-2 text-2xs italic text-muted-foreground/60">
            Кликните, чтобы привязать
          </p>
        )}
      </div>
    </div>
  );
}
