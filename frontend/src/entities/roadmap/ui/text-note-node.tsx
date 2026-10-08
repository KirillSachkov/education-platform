"use client";

import { cn } from "@/shared/lib/css";
import { Handle, Position, type NodeProps } from "@xyflow/react";
import { useRef, useState } from "react";
import type { RoadmapReactFlowNodeData } from "../lib/transforms";
import { safeParseNodeData } from "../lib/node-types";
import type { TextNoteData } from "../types";

const handleClass = "!w-3 !h-3 !bg-primary/0 !border-2 !border-transparent !rounded-full !transition-all group-hover:!bg-primary group-hover:!border-primary/50";

const FONT_SIZES = ["text-sm", "text-base", "text-lg", "text-xl", "text-2xl", "text-3xl", "text-4xl"] as const;
const SIZE_INDEX_DEFAULT = 3; // text-xl

export function TextNoteNode({
  data,
  selected,
  id,
}: NodeProps & { data: RoadmapReactFlowNodeData }) {
  const parsed = safeParseNodeData<TextNoteData>(data.rawData, { text: "" });
  const textareaRef = useRef<HTMLTextAreaElement>(null);
  const [editing, setEditing] = useState(false);

  // fontSize stored as index into FONT_SIZES array (0-6)
  const sizeIdx = typeof parsed.fontSize === "number"
    ? Math.max(0, Math.min(FONT_SIZES.length - 1, parsed.fontSize))
    : SIZE_INDEX_DEFAULT;

  function autoResize() {
    const el = textareaRef.current;
    if (!el) return;
    el.style.height = "auto";
    el.style.height = el.scrollHeight + "px";
  }

  function enterEditMode() {
    setEditing(true);
    window.dispatchEvent(
      new CustomEvent("roadmap-node-lock-drag", { detail: { nodeId: id, locked: true } }),
    );
    requestAnimationFrame(() => {
      textareaRef.current?.focus();
      autoResize();
    });
  }

  function exitEditMode() {
    setEditing(false);
    window.dispatchEvent(
      new CustomEvent("roadmap-node-lock-drag", { detail: { nodeId: id, locked: false } }),
    );
    const value = textareaRef.current?.value ?? "";
    if (!value.trim()) {
      window.dispatchEvent(
        new CustomEvent("roadmap-node-delete-empty", { detail: { nodeId: id } }),
      );
    }
  }

  function handleChange(e: React.ChangeEvent<HTMLTextAreaElement>) {
    autoResize();
    dispatchUpdate(e.target.value, sizeIdx);
  }

  function handleKeyDown(e: React.KeyboardEvent) {
    e.stopPropagation();
    if (e.key === "Escape") exitEditMode();
  }

  function dispatchUpdate(text: string, fontSize: number) {
    window.dispatchEvent(
      new CustomEvent("roadmap-node-text-change", {
        detail: { nodeId: id, text, fontSize },
      }),
    );
  }

  function handleWheel(e: React.WheelEvent) {
    if (!e.ctrlKey && !e.metaKey) return;
    e.preventDefault();
    e.stopPropagation();
    const newIdx = e.deltaY < 0
      ? Math.min(sizeIdx + 1, FONT_SIZES.length - 1)
      : Math.max(sizeIdx - 1, 0);
    if (newIdx !== sizeIdx) dispatchUpdate(parsed.text, newIdx);
  }

  function changeSizeBy(delta: number) {
    const newIdx = Math.max(0, Math.min(FONT_SIZES.length - 1, sizeIdx + delta));
    if (newIdx !== sizeIdx) dispatchUpdate(parsed.text, newIdx);
  }

  const readOnly = !!data.readOnly;

  return (
    <div
      className={cn(
        "group relative min-w-[60px]",
        selected && "rounded-lg ring-2 ring-primary",
        !editing && !readOnly && "cursor-grab",
      )}
      onDoubleClick={readOnly ? undefined : enterEditMode}
      onWheel={readOnly ? undefined : handleWheel}
    >
      {!readOnly && (
        <>
          <Handle type="source" position={Position.Top} id="top" className={handleClass} />
          <Handle type="source" position={Position.Bottom} id="bottom" className={handleClass} />
          <Handle type="source" position={Position.Left} id="left" className={handleClass} />
          <Handle type="source" position={Position.Right} id="right" className={handleClass} />
        </>
      )}

      {/* Size controls — visible when selected */}
      {selected && (
        <div className="absolute -top-8 left-0 z-10 flex items-center gap-0.5 rounded border border-border bg-card px-1 py-0.5 shadow-sm">
          <button
            type="button"
            onMouseDown={(e) => e.stopPropagation()}
            onClick={() => changeSizeBy(-1)}
            disabled={sizeIdx <= 0}
            className="px-1 text-xs text-muted-foreground hover:text-foreground disabled:opacity-30"
          >
            A-
          </button>
          <span className="px-1 text-2xs text-muted-foreground">{sizeIdx + 1}/{FONT_SIZES.length}</span>
          <button
            type="button"
            onMouseDown={(e) => e.stopPropagation()}
            onClick={() => changeSizeBy(1)}
            disabled={sizeIdx >= FONT_SIZES.length - 1}
            className="px-1 text-xs text-muted-foreground hover:text-foreground disabled:opacity-30"
          >
            A+
          </button>
        </div>
      )}

      {editing ? (
        <textarea
          ref={textareaRef}
          defaultValue={parsed.text}
          onChange={handleChange}
          onBlur={exitEditMode}
          onKeyDown={handleKeyDown}
          placeholder="Введите текст..."
          className={cn(
            "w-full resize-none border-none bg-transparent font-bold leading-snug text-foreground outline-none placeholder:text-muted-foreground/40",
            FONT_SIZES[sizeIdx],
          )}
          style={{ overflow: "hidden" }}
        />
      ) : (
        <p
          className={cn(
            "w-full whitespace-pre-wrap break-words font-bold leading-snug text-foreground",
            FONT_SIZES[sizeIdx],
            !parsed.text && "text-muted-foreground/40",
          )}
        >
          {parsed.text || "Введите текст..."}
        </p>
      )}
    </div>
  );
}
