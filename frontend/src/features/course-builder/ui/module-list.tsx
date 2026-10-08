"use client";

import type { BuilderSectionDto, CourseBuilderDto } from "@/entities/course";
import type { ModuleItemDto } from "@/entities/module";
import { tagsApi } from "@/entities/tag";
import { EntityTypes } from "@/shared/config/entity-types";
import { Button } from "@/shared/ui/kit/button";
import {
  Command,
  CommandEmpty,
  CommandGroup,
  CommandInput,
  CommandItem,
  CommandList,
} from "@/shared/ui/kit/command";
import { Popover, PopoverContent, PopoverTrigger } from "@/shared/ui/kit/popover";
import { cn } from "@/shared/lib/css";
import { compareSortKey } from "@/shared/lib/sort-key";
import { DragDropProvider } from "@dnd-kit/react";
import { isSortable } from "@dnd-kit/react/sortable";
import { BookOpen, Check, ChevronsUpDown, Compass, Plus, X } from "lucide-react";
import { useRef, useState } from "react";
import { toast } from "sonner";
import { MaterialPickerDialog } from "@/entities/material";
import { QuizPickerDialog } from "@/entities/quiz";
import { useAttachIssueToModule } from "../model/use-attach-issue-to-module";
import { useAttachExistingMaterialToModule } from "../model/use-attach-existing-material-to-module";
import { useAttachQuizToModule } from "../model/use-attach-quiz-to-module";
import { useMoveModuleItem } from "../model/use-move-module-item";
import { useSetGettingStartedModule } from "../model/use-set-getting-started-module";
import { useTransferModuleItem } from "../model/use-transfer-module-item";
import { CreateModuleDialog } from "./create-module-dialog";
import { EditModuleDialog } from "./edit-module-dialog";
import { IssuePickerDialog } from "./issue-picker-dialog";
import { ModuleCard } from "./module-card";

interface ModuleListProps {
  courseId: string;
  course: CourseBuilderDto;
  modules: BuilderSectionDto[];
  projects: BuilderSectionDto[];
  onCreateModule: (data: { title: string; description: string | null }) => Promise<unknown>;
  isCreatePending: boolean;
  onUpdateModule: (
    moduleId: string,
    data: { title: string; description: string | null; detailedDescription: string | null },
  ) => Promise<unknown>;
  onDetach: (referenceId: string) => Promise<unknown>;
  isDetachPending: boolean;
  onMove: (
    items: BuilderSectionDto[],
    referenceId: string,
    oldIndex: number,
    newIndex: number,
  ) => Promise<void>;
}

export function ModuleList({
  courseId,
  course,
  modules,
  projects,
  onCreateModule,
  isCreatePending,
  onUpdateModule,
  onDetach,
  isDetachPending,
  onMove,
}: ModuleListProps) {
  const [createModuleOpen, setCreateModuleOpen] = useState(false);
  const [gsOpen, setGsOpen] = useState(false);
  const setGsModule = useSetGettingStartedModule(courseId);
  const gsModuleId = course.gettingStartedModuleId;
  const gsModuleTitle = gsModuleId
    ? (modules.find((m) => m.id === gsModuleId)?.title ?? null)
    : null;
  const [attachIssueModuleId, setAttachIssueModuleId] = useState<string | null>(null);
  const [attachMaterialModuleId, setAttachMaterialModuleId] = useState<string | null>(null);
  const [attachQuizModuleId, setAttachQuizModuleId] = useState<string | null>(null);
  const [editModuleData, setEditModuleData] = useState<{
    moduleId: string;
    title: string;
    description: string | null;
    detailedDescription: string | null;
  } | null>(null);
  const { moveItem } = useMoveModuleItem(courseId);
  const { transferItem } = useTransferModuleItem(courseId);

  // Original DOM parent of the currently dragged item. OptimisticSortingPlugin
  // physically moves the node across container boundaries via insertAdjacentElement
  // to animate reorder previews. On cross-module drops we restore the node back
  // here before React reconciles the refetch — otherwise React's
  // parentA.removeChild(node) throws NotFoundError because the node is now a
  // child of parentB. See #213 for why we no longer pair this with an optimistic
  // cache write.
  const itemOriginalParentRef = useRef<Element | null>(null);

  const getModuleItems = (moduleId: string): ModuleItemDto[] => {
    const mod = modules.find((m) => m.id === moduleId);
    if (!mod) return [];
    return [...mod.items].sort((a, b) => compareSortKey(a.sortKey, b.sortKey));
  };

  /**
   * Compute afterSortKey/beforeSortKey for inserting at `newIndex` in a sorted item list.
   * For within-module moves, exclude the dragged item so it doesn't appear as its own neighbour.
   */
  const computeNeighbourSortKeys = (
    items: ModuleItemDto[],
    newIndex: number,
    excludeReferenceId?: string,
  ) => {
    const list = excludeReferenceId
      ? items.filter((i) => i.referenceId !== excludeReferenceId)
      : items;
    return {
      afterSortKey: newIndex > 0 ? list[newIndex - 1]?.sortKey : undefined,
      beforeSortKey: newIndex < list.length ? list[newIndex]?.sortKey : undefined,
    };
  };

  const handleMoveWithinModule = async (
    moduleId: string,
    referenceId: string,
    _initialIndex: number,
    newIndex: number,
  ) => {
    const items = getModuleItems(moduleId);
    const { afterSortKey, beforeSortKey } = computeNeighbourSortKeys(items, newIndex, referenceId);
    await moveItem({ moduleId, referenceId, request: { afterSortKey, beforeSortKey } });
  };

  const handleTransferBetweenModules = async (
    sourceModuleId: string,
    targetModuleId: string,
    referenceId: string,
    newIndex: number,
  ) => {
    const targetItems = getModuleItems(targetModuleId);
    const { afterSortKey, beforeSortKey } = computeNeighbourSortKeys(targetItems, newIndex);

    await transferItem({
      sourceModuleId,
      referenceId,
      request: { targetModuleId, afterSortKey, beforeSortKey },
    });
  };

  return (
    <div>
      <div className="flex items-center justify-between gap-3 mb-5">
        <div className="min-w-0">
          <h2 className="text-base font-semibold">Структура курса</h2>
          <p className="text-sm text-muted-foreground mt-0.5">
            Организуйте содержание курса по модулям
          </p>
        </div>
        <Button
          size="sm"
          className="bg-gradient-primary text-primary-foreground border-0 hover:opacity-90 shrink-0"
          onClick={() => setCreateModuleOpen(true)}
          disabled={isCreatePending}
        >
          <Plus size={14} /> <span className="hidden sm:inline">Модуль</span>
        </Button>
      </div>

      {modules.length > 0 && (
        <div className="flex items-center gap-2 mb-4 min-w-0">
          <Compass size={14} className="text-muted-foreground shrink-0" />
          <span className="text-xs text-muted-foreground shrink-0">Начни здесь:</span>
          <Popover open={gsOpen} onOpenChange={setGsOpen}>
            <PopoverTrigger asChild>
              <Button
                variant="outline"
                size="sm"
                className="h-7 min-w-0 flex-1 justify-between gap-1 font-normal text-xs sm:max-w-64 sm:flex-none"
              >
                <span className="truncate">{gsModuleTitle ?? "Не выбран"}</span>
                <ChevronsUpDown size={12} className="text-muted-foreground shrink-0" />
              </Button>
            </PopoverTrigger>
            <PopoverContent className="w-72 p-0" align="start">
              <Command>
                <CommandInput placeholder="Найти модуль..." />
                <CommandList>
                  <CommandEmpty>Модули не найдены</CommandEmpty>
                  <CommandGroup>
                    {modules.map((m) => (
                      <CommandItem
                        key={m.id}
                        value={m.title}
                        onSelect={() => {
                          setGsModule.mutate(m.id === gsModuleId ? null : m.id);
                          setGsOpen(false);
                        }}
                      >
                        <Check
                          size={14}
                          className={cn(
                            "shrink-0",
                            m.id === gsModuleId ? "opacity-100" : "opacity-0",
                          )}
                        />
                        <span className="truncate">{m.title}</span>
                      </CommandItem>
                    ))}
                  </CommandGroup>
                </CommandList>
              </Command>
            </PopoverContent>
          </Popover>
          {gsModuleId && (
            <Button
              variant="ghost"
              size="icon"
              className="size-7 text-muted-foreground hover:text-destructive"
              onClick={() => setGsModule.mutate(null)}
              disabled={setGsModule.isPending}
            >
              <X size={12} />
            </Button>
          )}
        </div>
      )}

      {modules.length === 0 && (
        <div className="border-2 border-dashed rounded-2xl p-10 flex flex-col items-center justify-center text-center">
          <div className="size-12 rounded-xl bg-muted flex items-center justify-center mb-3">
            <BookOpen size={22} className="text-muted-foreground" />
          </div>
          <p className="text-sm font-medium mb-1">Нет модулей</p>
          <p className="text-sm text-muted-foreground mb-4">
            Создайте первый модуль для структурирования курса
          </p>
          <Button
            variant="outline"
            size="sm"
            onClick={() => setCreateModuleOpen(true)}
            disabled={isCreatePending}
          >
            <Plus size={14} /> Создать модуль
          </Button>
        </div>
      )}

      <DragDropProvider
        onDragStart={(event) => {
          const { source } = event.operation;
          if (isSortable(source) && source.type === "item") {
            itemOriginalParentRef.current = source.element?.parentElement ?? null;
          }
        }}
        onDragEnd={(event) => {
          const { source, target } = event.operation;

          // Restore the DOM before React reconciles the refetch.
          // OptimisticSortingPlugin moved source.element across container
          // boundaries via insertAdjacentElement to animate the drag preview;
          // if we leave it there, React.removeChild on the old parent throws.
          if (
            isSortable(source) &&
            source.type === "item" &&
            source.element &&
            source.element.isConnected &&
            itemOriginalParentRef.current?.isConnected &&
            source.element.parentElement !== itemOriginalParentRef.current
          ) {
            itemOriginalParentRef.current.appendChild(source.element);
          }
          itemOriginalParentRef.current = null;

          if (event.canceled) return;
          if (!isSortable(source)) return;
          const { initialIndex, index: newIndex } = source;

          // Defer mutations to a microtask so React Query cache updates don't
          // run inside dnd-kit's trackRendering → startTransition commit, which
          // triggers React's "useInsertionEffect must not schedule updates"
          // guard when the item remounts in a different module.
          if (source.type === "module") {
            if (initialIndex === newIndex) return;
            const sourceId = source.id as string;
            queueMicrotask(() => {
              onMove(modules, sourceId, initialIndex, newIndex);
            });
          } else if (source.type === "item") {
            // initialGroup = moduleId where item started, group = moduleId where it was dropped
            const sourceGroup = String(source.initialGroup);
            const targetGroup = String(source.group);
            const sourceId = source.id as string;

            // Check if dropped on an empty module's drop zone (useDroppable, not useSortable)
            const dropZoneId = target && "id" in target ? String(target.id) : "";
            const dropZoneMatch = dropZoneId.match(/^drop-zone-(.+)$/);

            if (dropZoneMatch && dropZoneMatch[1] !== sourceGroup) {
              // Dropped on a module's drop zone — transfer and append to end
              const targetModuleId = dropZoneMatch[1];
              queueMicrotask(() => {
                handleTransferBetweenModules(sourceGroup, targetModuleId, sourceId, Infinity);
              });
            } else if (sourceGroup === targetGroup) {
              if (initialIndex === newIndex) return;
              queueMicrotask(() => {
                handleMoveWithinModule(sourceGroup, sourceId, initialIndex, newIndex);
              });
            } else {
              queueMicrotask(() => {
                handleTransferBetweenModules(sourceGroup, targetGroup, sourceId, newIndex);
              });
            }
          }
        }}
      >
        <div className="space-y-4">
          {(() => {
            // Pre-compute global item offsets for cross-course numbering
            const offsets: number[] = [];
            let offset = 0;
            for (const m of modules) {
              offsets.push(offset);
              offset += m.items.length;
            }
            return modules.map((module, mIdx) => (
              <ModuleCard
                key={module.id}
                module={module}
                index={mIdx}
                courseId={courseId}
                globalItemOffset={offsets[mIdx]}
                items={module.items}
                description={module.description}
                detailedDescription={module.detailedDescription}
                onEdit={(detail) =>
                  setEditModuleData({
                    ...detail,
                    moduleId: module.id,
                  })
                }
                onDetach={() => onDetach(module.id)}
                isDetachPending={isDetachPending}
                onAttachIssue={() => setAttachIssueModuleId(module.id)}
                isAttachIssuePending={false}
                onAddMaterial={() => setAttachMaterialModuleId(module.id)}
                onAddQuiz={() => setAttachQuizModuleId(module.id)}
                isFirst={mIdx === 0}
                isLast={mIdx === modules.length - 1}
                onMoveUp={() => onMove(modules, module.id, mIdx, mIdx - 1)}
                onMoveDown={() => onMove(modules, module.id, mIdx, mIdx + 1)}
                onMoveItem={(referenceId, fromIdx, toIdx) =>
                  handleMoveWithinModule(module.id, referenceId, fromIdx, toIdx)
                }
              />
            ));
          })()}
        </div>
      </DragDropProvider>

      {modules.length > 0 && (
        <Button
          variant="outline"
          onClick={() => setCreateModuleOpen(true)}
          className="w-full mt-3"
          disabled={isCreatePending}
        >
          <Plus size={14} /> Добавить модуль
        </Button>
      )}

      <CreateModuleDialog
        open={createModuleOpen}
        onOpenChange={setCreateModuleOpen}
        onSubmit={(data) => {
          // Цепочка работает в фоне; форма уже закрыта диалогом.
          void (async () => {
            const result = (await onCreateModule({
              title: data.title,
              description: data.description,
            }).catch(() => undefined)) as { result?: string } | undefined;
            if (data.tags.length > 0 && result?.result) {
              await tagsApi
                .addTagsToEntity({
                  entityType: EntityTypes.MODULE,
                  entityId: result.result,
                  tagTitles: data.tags,
                  tagIds: [],
                })
                .catch(() => {
                  toast.error("Ошибка привязки тегов");
                });
            }
          })();
        }}
      />

      {editModuleData && (
        <EditModuleDialog
          moduleId={editModuleData.moduleId}
          open={!!editModuleData}
          onOpenChange={(open) => {
            if (!open) setEditModuleData(null);
          }}
          initialData={{
            title: editModuleData.title,
            description: editModuleData.description,
            detailedDescription: editModuleData.detailedDescription,
          }}
          onSubmit={(data) => {
            void onUpdateModule(editModuleData.moduleId, data);
            setEditModuleData(null);
          }}
        />
      )}

      {attachIssueModuleId && (
        <AttachIssueToModuleDialog
          courseId={courseId}
          moduleId={attachIssueModuleId}
          courseItems={projects}
          open={!!attachIssueModuleId}
          onOpenChange={(open) => {
            if (!open) setAttachIssueModuleId(null);
          }}
        />
      )}

      {attachMaterialModuleId && (
        <AttachMaterialToModuleDialog
          courseId={courseId}
          moduleId={attachMaterialModuleId}
          excludedIds={
            new Set(
              modules
                .find((m) => m.id === attachMaterialModuleId)
                ?.items.filter((i) => i.itemType === "Material")
                .map((i) => i.referenceId) ?? [],
            )
          }
          open={!!attachMaterialModuleId}
          onOpenChange={(open) => {
            if (!open) setAttachMaterialModuleId(null);
          }}
        />
      )}

      {attachQuizModuleId && (
        <AttachQuizToModuleDialog
          moduleId={attachQuizModuleId}
          excludedIds={
            new Set(
              modules
                .find((m) => m.id === attachQuizModuleId)
                ?.items.filter((i) => i.itemType === "Quiz")
                .map((i) => i.referenceId) ?? [],
            )
          }
          open={!!attachQuizModuleId}
          onOpenChange={(open) => {
            if (!open) setAttachQuizModuleId(null);
          }}
        />
      )}
    </div>
  );
}

function AttachMaterialToModuleDialog({
  courseId,
  moduleId,
  excludedIds,
  open,
  onOpenChange,
}: {
  courseId: string;
  moduleId: string;
  excludedIds: Set<string>;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) {
  const { attachMaterial, isPending } = useAttachExistingMaterialToModule(moduleId);

  const handleSelect = async (materialId: string) => {
    await attachMaterial(materialId);
    onOpenChange(false);
  };

  return (
    <MaterialPickerDialog
      open={open}
      onOpenChange={onOpenChange}
      title="Добавить материал в модуль"
      description="Выберите существующий материал или создайте новый"
      courseId={courseId}
      moduleId={moduleId}
      onSelect={handleSelect}
      isPending={isPending}
      excludedIds={excludedIds}
    />
  );
}

function AttachQuizToModuleDialog({
  moduleId,
  excludedIds,
  open,
  onOpenChange,
}: {
  moduleId: string;
  excludedIds: Set<string>;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) {
  const { attachQuiz, isPending } = useAttachQuizToModule(moduleId);

  const handleSelect = async (quizId: string) => {
    await attachQuiz(quizId);
    onOpenChange(false);
  };

  return (
    <QuizPickerDialog
      open={open}
      onOpenChange={onOpenChange}
      title="Добавить тест в модуль"
      description="Выберите тест из библиотеки — он станет элементом программы модуля"
      onSelect={(quizId) => void handleSelect(quizId)}
      isPending={isPending}
      excludedIds={excludedIds}
    />
  );
}

function AttachIssueToModuleDialog({
  courseId,
  moduleId,
  courseItems,
  open,
  onOpenChange,
}: {
  courseId: string;
  moduleId: string;
  courseItems: BuilderSectionDto[];
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) {
  const { attachIssue, isPending } = useAttachIssueToModule(courseId, moduleId);

  const handleSelect = async (issueId: string) => {
    await attachIssue(issueId);
    onOpenChange(false);
  };

  return (
    <IssuePickerDialog
      open={open}
      onOpenChange={onOpenChange}
      courseItems={courseItems}
      onSelect={handleSelect}
      isPending={isPending}
    />
  );
}
