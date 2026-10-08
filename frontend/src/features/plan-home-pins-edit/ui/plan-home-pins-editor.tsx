"use client";

import { type HomePinListItemDto, planHomePinsQueryOptions } from "@/entities/plan-pinned-material";
import { MaterialPickerDialog } from "@/entities/material";
import { Button } from "@/shared/ui/kit/button";
import { Card } from "@/shared/ui/kit/card";
import { Icons } from "@/shared/ui/icons";
import { DeleteConfirmDialog } from "@/shared/ui/components/delete-confirm-dialog";
import { DragDropProvider } from "@dnd-kit/react";
import { isSortable, useSortable } from "@dnd-kit/react/sortable";
import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import {
  useAddHomePin,
  useDeleteHomePin,
  useReorderHomePin,
} from "../model/use-home-pins-mutations";
import { HomePinNoteDialog } from "./home-pin-note-dialog";

type Props = { planId: string };

/**
 * Author-side редактор закреплённых материалов плана (epic #397, S8). Список текущих
 * закрепов, добавление через `MaterialPickerDialog`, редактирование заметки, drag-reorder
 * (@dnd-kit/react v10, как в OnboardingFlowEditor) и удаление. Закрепы показываются
 * студентам на /home (секция «Закреплено автором»).
 */
export function PlanHomePinsEditor({ planId }: Props) {
  const { data: pins, isPending } = useQuery(planHomePinsQueryOptions(planId));
  const addPin = useAddHomePin(planId);
  const reorderPin = useReorderHomePin(planId);

  const [isPickerOpen, setIsPickerOpen] = useState(false);
  const [editingPin, setEditingPin] = useState<HomePinListItemDto | null>(null);

  if (isPending || !pins) {
    return <div className="text-muted-foreground">Загрузка…</div>;
  }

  const excludedIds = new Set(pins.map((p) => p.materialId));

  return (
    <div className="space-y-6">
      <header className="space-y-1">
        <p className="text-sm text-muted-foreground">
          Закрепи ключевые материалы — ученики с доступом к этому плану увидят их на
          главной в блоке «Закреплено автором». Помогает направить, с чего начать.
        </p>
      </header>

      <section className="space-y-3">
        <div className="flex items-center justify-between">
          <h2 className="text-lg font-medium">Закрепы</h2>
          <Button size="sm" onClick={() => setIsPickerOpen(true)} disabled={addPin.isPending}>
            <Icons.add className="size-4" />
            Закрепить материал
          </Button>
        </div>

        {pins.length === 0 ? (
          <Card className="p-6 text-center text-sm text-muted-foreground">
            Пока ничего не закреплено. Выбери материал, который ученики увидят первым.
          </Card>
        ) : (
          <DragDropProvider
            onDragEnd={(event) => {
              if (event.canceled) return;
              const { source } = event.operation;
              if (!isSortable(source)) return;
              const { initialIndex, index } = source;
              if (initialIndex === index) return;

              const pinId = source.id as string;
              // Новые before/after из массива без перемещаемого элемента.
              const others = pins.filter((p) => p.pinId !== pinId);
              const beforeId = index > 0 ? others[index - 1].pinId : null;
              const afterId = index < others.length ? others[index].pinId : null;
              reorderPin.mutate({ pinId, request: { beforeId, afterId } });
            }}
          >
            <ol className="space-y-2">
              {pins.map((pin, idx) => (
                <SortablePinRow
                  key={pin.pinId}
                  planId={planId}
                  pin={pin}
                  index={idx}
                  onEditNote={() => setEditingPin(pin)}
                />
              ))}
            </ol>
          </DragDropProvider>
        )}
      </section>

      <MaterialPickerDialog
        open={isPickerOpen}
        onOpenChange={setIsPickerOpen}
        title="Закрепить материал"
        description="Выбери материал — он появится на главной у учеников этого плана."
        onSelect={(materialId) => {
          addPin.mutate({ materialId, note: null });
          setIsPickerOpen(false);
        }}
        isPending={addPin.isPending}
        excludedIds={excludedIds}
      />

      <HomePinNoteDialog
        planId={planId}
        pin={editingPin}
        open={editingPin !== null}
        onOpenChange={(open) => {
          if (!open) setEditingPin(null);
        }}
      />
    </div>
  );
}

type SortablePinRowProps = {
  planId: string;
  pin: HomePinListItemDto;
  index: number;
  onEditNote: () => void;
};

function SortablePinRow({ planId, pin, index, onEditNote }: SortablePinRowProps) {
  const { ref, handleRef, isDragging } = useSortable({ id: pin.pinId, index });
  const deletePin = useDeleteHomePin(planId);

  const title = pin.title.trim() || "Без названия";

  return (
    <li ref={ref} className={isDragging ? "opacity-50" : undefined}>
      <Card className="flex flex-col gap-3 p-4 sm:flex-row sm:items-center">
        <button
          ref={handleRef}
          type="button"
          className="cursor-grab touch-none self-start text-muted-foreground hover:text-foreground"
          aria-label="Перетащить"
        >
          <Icons.drag className="h-4 w-4" />
        </button>
        <div className="min-w-0 flex-1">
          <div className="text-xs font-medium text-muted-foreground">{index + 1}.</div>
          <div className="truncate font-medium">{title}</div>
          {pin.note ? (
            <p className="mt-0.5 line-clamp-2 text-sm text-muted-foreground">{pin.note}</p>
          ) : null}
        </div>
        <div className="flex gap-1 self-start sm:self-center">
          <Button size="sm" variant="ghost" onClick={onEditNote}>
            {pin.note ? "Заметка" : "+ Заметка"}
          </Button>
          <DeleteConfirmDialog
            title="Открепить материал?"
            description="Материал пропадёт из блока «Закреплено автором» у учеников этого плана. Сам материал не удаляется."
            confirmLabel="Открепить"
            isPending={deletePin.isPending}
            onConfirm={() => deletePin.mutate(pin.pinId)}
            trigger={
              <Button size="sm" variant="ghost" aria-label="Открепить">
                <Icons.delete className="size-4" />
              </Button>
            }
          />
        </div>
      </Card>
    </li>
  );
}
