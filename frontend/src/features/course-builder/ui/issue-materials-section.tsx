"use client";

import type { InternalMaterialItem, IssueInternalMaterialDto } from "@/entities/issue";
import { MaterialPickerDialog } from "@/entities/material";
import { Button } from "@/shared/ui/kit/button";
import { BookOpen, Plus, Trash2 } from "lucide-react";
import { useState } from "react";
import { useUpdateIssueMaterials } from "../model/use-update-issue-materials";

interface IssueMaterialsSectionProps {
  issueId: string;
  projectId: string;
  courseId: string;
  materials: IssueInternalMaterialDto[];
}

export function IssueMaterialsSection({
  issueId,
  projectId,
  courseId,
  materials,
}: IssueMaterialsSectionProps) {
  const { updateMaterials, isPending } = useUpdateIssueMaterials(issueId, projectId);

  const [pickerOpen, setPickerOpen] = useState(false);

  const attachedIds = new Set(materials.map((m) => m.referenceId));

  const addMaterial = async (materialId: string) => {
    const newItems: InternalMaterialItem[] = [
      ...materials.map((m) => ({
        itemType: m.itemType,
        referenceId: m.referenceId,
        isRequired: m.isRequired,
      })),
      { itemType: "Material", referenceId: materialId, isRequired: false },
    ];
    await updateMaterials({ items: newItems });
    setPickerOpen(false);
  };

  const removeMaterial = async (referenceId: string) => {
    const newItems: InternalMaterialItem[] = materials
      .filter((m) => m.referenceId !== referenceId)
      .map((m) => ({
        itemType: m.itemType,
        referenceId: m.referenceId,
        isRequired: m.isRequired,
      }));
    await updateMaterials({ items: newItems });
  };

  return (
    <div>
      <div className="flex items-center justify-between mb-3">
        <div className="flex items-center gap-1.5 text-sm font-medium">
          <BookOpen size={14} className="text-muted-foreground" />
          Материалы
        </div>

        <Button
          type="button"
          variant="outline"
          size="icon"
          className="size-7"
          onClick={() => setPickerOpen(true)}
          disabled={isPending}
        >
          <Plus size={14} />
        </Button>
      </div>

      {materials.length === 0 ? (
        <p className="text-sm text-muted-foreground">Нет прикреплённых материалов</p>
      ) : (
        <div className="space-y-1">
          {materials.map((m) => (
            <div
              key={m.referenceId}
              className="group flex items-center gap-2 px-3 py-2 rounded-lg bg-accent"
            >
              <BookOpen size={14} className="text-muted-foreground shrink-0" />
              <span className="text-sm truncate flex-1">{m.title ?? "Без названия"}</span>
              <button
                type="button"
                onClick={() => removeMaterial(m.referenceId)}
                disabled={isPending}
                title="Удалить"
                className="size-7 rounded-md flex items-center justify-center text-muted-foreground hover:text-red hover:bg-red/10 transition-colors sm:opacity-0 sm:group-hover:opacity-100"
              >
                <Trash2 size={14} />
              </button>
            </div>
          ))}
        </div>
      )}

      <MaterialPickerDialog
        open={pickerOpen}
        onOpenChange={setPickerOpen}
        title="Добавить материал"
        description="Выберите существующий материал или создайте новый."
        courseId={courseId}
        onSelect={addMaterial}
        isPending={isPending}
        excludedIds={attachedIds}
      />
    </div>
  );
}
