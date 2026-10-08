"use client";

import { Button } from "@/shared/ui/kit/button";
import { Popover, PopoverContent, PopoverTrigger } from "@/shared/ui/kit/popover";
import { cn } from "@/shared/lib/css";
import { pluralize } from "@/shared/lib/pluralize";
import {
  BookOpen,
  Check,
  HelpCircle,
  Layers,
  Loader2,
  Maximize2,
  Redo2,
  Save,
  StickyNote,
  Undo2,
} from "lucide-react";

interface EditorToolbarProps {
  isDirty: boolean;
  isSaving: boolean;
  canUndo: boolean;
  canRedo: boolean;
  nodeCount: number;
  onSave: () => void;
  onUndo: () => void;
  onRedo: () => void;
  onAddNode: (type: string) => void;
  onFitView: () => void;
}

const NODE_PALETTE = [
  { type: "EntityReference", icon: BookOpen, label: "Материал", shortcut: "1" },
  { type: "TextNote", icon: StickyNote, label: "Текст", shortcut: "2" },
  { type: "Group", icon: Layers, label: "Группа", shortcut: "3" },
] as const;

const SHORTCUTS = [
  { keys: "Ctrl/⌘ + Z", label: "Отменить" },
  { keys: "Ctrl/⌘ + ⇧ + Z", label: "Повторить" },
  { keys: "Ctrl/⌘ + S", label: "Сохранить" },
  { keys: "Delete", label: "Удалить выбранное" },
  { keys: "Shift + Drag", label: "Выделение области" },
  { keys: "1 / 2 / 3", label: "Добавить элемент" },
  { keys: "Hover + Drag", label: "Соединить элементы" },
  { keys: "Double Click", label: "Редактировать текст" },
];

export function EditorToolbar({
  isDirty,
  isSaving,
  canUndo,
  canRedo,
  nodeCount,
  onSave,
  onUndo,
  onRedo,
  onAddNode,
  onFitView,
}: EditorToolbarProps) {
  return (
    <div className="flex w-full min-w-0 flex-wrap items-center gap-1 border-b bg-card px-2 py-1">
      {/* Undo / Redo */}
      <Button
        variant="ghost"
        size="icon"
        onClick={onUndo}
        disabled={!canUndo}
        className="size-11 sm:size-8"
        title="Отменить (⌘Z)"
      >
        <Undo2 className="size-4" />
      </Button>
      <Button
        variant="ghost"
        size="icon"
        onClick={onRedo}
        disabled={!canRedo}
        className="size-11 sm:size-8"
        title="Повторить (⌘⇧Z)"
      >
        <Redo2 className="size-4" />
      </Button>

      <div className="mx-1.5 h-4 w-px bg-border" />

      {/* Node palette */}
      {NODE_PALETTE.map((item) => (
        <Button
          key={item.type}
          variant="ghost"
          size="sm"
          className="min-h-11 gap-1.5 text-xs sm:min-h-0"
          onClick={() => onAddNode(item.type)}
          title={`${item.label} (${item.shortcut})`}
        >
          <item.icon className="size-3.5" />
          <span className="hidden sm:inline">{item.label}</span>
        </Button>
      ))}

      <div className="mx-1.5 h-4 w-px bg-border" />

      {/* Save */}
      <Button
        variant="ghost"
        size="sm"
        onClick={onSave}
        disabled={isSaving}
        className="min-h-11 gap-1.5 sm:min-h-0"
        title="Сохранить (⌘S)"
      >
        {isSaving ? <Loader2 className="size-3.5 animate-spin" /> : <Save className="size-3.5" />}
        <span className="hidden sm:inline">Сохранить</span>
      </Button>

      {/* Status */}
      <div className="ml-1 flex items-center gap-1.5">
        {isSaving && <span className="text-2xs text-muted-foreground">Сохранение...</span>}
        {isDirty && !isSaving && <span className="text-2xs text-yellow-500">Изменения</span>}
        {!isDirty && !isSaving && (
          <span className="flex items-center gap-0.5 text-2xs text-green-500">
            <Check className="size-3" />
            Сохранено
          </span>
        )}
      </div>

      {/* Spacer — only pushes trailing controls apart once the bar fits on one row (sm+). */}
      <div className="sm:flex-1" />

      {/* Fit view */}
      <Button
        variant="ghost"
        size="icon"
        className="size-11 sm:size-8"
        onClick={onFitView}
        title="Показать всё"
      >
        <Maximize2 className="size-4" />
      </Button>

      {/* Node count */}
      <span className="text-2xs text-muted-foreground">
        {nodeCount} {pluralize(nodeCount, "элемент", "элемента", "элементов")}
      </span>

      <div className="mx-1.5 h-4 w-px bg-border" />

      {/* Shortcuts help */}
      <Popover>
        <PopoverTrigger asChild>
          <Button variant="ghost" size="icon" className="size-11 sm:size-8" title="Горячие клавиши">
            <HelpCircle className="size-4" />
          </Button>
        </PopoverTrigger>
        <PopoverContent align="end" className="w-56 p-3">
          <p className="mb-2 text-xs font-semibold">Горячие клавиши</p>
          <div className="space-y-1.5">
            {SHORTCUTS.map((s) => (
              <div key={s.keys} className="flex items-center justify-between">
                <span className="text-xs text-muted-foreground">{s.label}</span>
                <kbd className={cn("rounded border bg-muted px-1.5 py-0.5 text-2xs font-mono")}>
                  {s.keys}
                </kbd>
              </div>
            ))}
          </div>
        </PopoverContent>
      </Popover>
    </div>
  );
}
