import { Loader2 } from "lucide-react";
import type { ReactNode } from "react";

import type { CommentId } from "@/entities/comment";
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

import { useDeleteComment } from "../model/use-delete-comment";

interface DeleteCommentDialogProps {
  commentId: CommentId;
  onSuccess?: () => void;
  open?: boolean;
  onOpenChange?: (open: boolean) => void;
  trigger?: ReactNode;
}

export const DeleteCommentDialog = ({
  commentId,
  onSuccess,
  open,
  onOpenChange,
  trigger,
}: DeleteCommentDialogProps) => {
  const { deleteComment, isPending } = useDeleteComment();

  return (
    <AlertDialog open={open} onOpenChange={onOpenChange}>
      {trigger && <AlertDialogTrigger asChild>{trigger}</AlertDialogTrigger>}
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>Удалить комментарий?</AlertDialogTitle>
          <AlertDialogDescription>
            Это действие нельзя отменить. Комментарий будет удалён навсегда.
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel disabled={isPending}>Отмена</AlertDialogCancel>
          <AlertDialogAction
            onClick={() =>
              deleteComment(commentId, {
                onSuccess: () => {
                  onOpenChange?.(false);
                  onSuccess?.();
                },
              })
            }
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
};
