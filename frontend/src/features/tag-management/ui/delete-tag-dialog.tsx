"use client";

import type { TagId } from "@/entities/tag";
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
  AlertDialogTrigger,
} from "@/shared/ui/kit/alert-dialog";
import { Button } from "@/shared/ui/kit/button";
import { Loader2, Trash2 } from "lucide-react";
import { useDeleteTag } from "../model/use-delete-tag";

interface DeleteTagDialogProps {
  tagId: TagId;
  tagTitle: string;
  variant?: "icon" | "button";
}

export function DeleteTagDialog({
  tagId,
  tagTitle,
  variant = "button",
}: DeleteTagDialogProps) {
  const { deleteTag, isPending } = useDeleteTag();

  const trigger =
    variant === "icon" ? (
      <Button
        variant="ghost"
        size="icon"
        className="h-8 w-8 text-destructive hover:text-destructive"
      >
        <Trash2 className="h-4 w-4" />
      </Button>
    ) : (
      <Button
        variant="ghost"
        size="sm"
        className="text-destructive hover:text-destructive"
      >
        <Trash2 size={13} />
        Удалить
      </Button>
    );

  return (
    <AlertDialog>
      <AlertDialogTrigger asChild>{trigger}</AlertDialogTrigger>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>Удалить тег «{tagTitle}»?</AlertDialogTitle>
          <AlertDialogDescription>
            Это действие нельзя отменить. Тег и его связи будут удалены.
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel disabled={isPending}>Отмена</AlertDialogCancel>
          <AlertDialogAction
            onClick={async () => {
              await deleteTag(tagId);
            }}
            disabled={isPending}
            className="bg-destructive text-destructive-foreground hover:bg-destructive/90"
          >
            {isPending && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
            Удалить
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}
