"use client";

import type { CollectionAccessType } from "@/entities/collection";
import {
  AccessTypeSelector,
  type ContentAccessType,
} from "@/shared/ui/components/access-type-selector";
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from "@/shared/ui/kit/alert-dialog";
import { Loader2 } from "lucide-react";
import { useState } from "react";
import { useBulkSetCollectionItemsAccess } from "../model/use-bulk-set-collection-items-access";

interface BulkAccessTypeDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  collectionId: string;
  /** AccessType подборки — стартовое значение для селектора. */
  currentAccessType: CollectionAccessType;
  /** Привязана ли подборка к курсу — влияет на подсказки селектора (orphan vs course). */
  hasCourseBinding: boolean;
}

export function BulkAccessTypeDialog({
  open,
  onOpenChange,
  collectionId,
  currentAccessType,
  hasCourseBinding,
}: BulkAccessTypeDialogProps) {
  const [selected, setSelected] = useState<ContentAccessType>(currentAccessType);
  const { bulkSetAccess, isPending } = useBulkSetCollectionItemsAccess();

  const handleApply = async () => {
    await bulkSetAccess({ collectionId, accessType: selected as CollectionAccessType });
    onOpenChange(false);
  };

  return (
    <AlertDialog open={open} onOpenChange={onOpenChange}>
      <AlertDialogContent className="sm:max-w-2xl">
        <AlertDialogHeader>
          <AlertDialogTitle>Сменить уровень доступа у всех материалов</AlertDialogTitle>
          <AlertDialogDescription asChild>
            <div className="space-y-2 text-sm text-muted-foreground">
              <p>
                Уровень доступа поменяется у <strong>каждого материала подборки</strong>, включая
                материалы во вложенных секциях.
              </p>
              <p className="text-yellow">
                Важно: материал может одновременно лежать в других курсах и подборках — там доступ
                тоже изменится. Материалы чужих авторов будут пропущены.
              </p>
            </div>
          </AlertDialogDescription>
        </AlertDialogHeader>

        <div className="py-2">
          <AccessTypeSelector
            value={selected}
            onChange={setSelected}
            hasCourseBinding={hasCourseBinding}
          />
        </div>

        <AlertDialogFooter>
          <AlertDialogCancel disabled={isPending}>Отмена</AlertDialogCancel>
          <AlertDialogAction
            onClick={(e) => {
              e.preventDefault();
              void handleApply();
            }}
            disabled={isPending}
          >
            {isPending && <Loader2 size={14} className="animate-spin" />}
            Применить ко всем
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}
