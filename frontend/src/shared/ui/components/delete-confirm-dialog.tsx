"use client";

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
import { Loader2 } from "lucide-react";
import type { ReactNode } from "react";

interface DeleteConfirmDialogProps {
  /** Headline question, e.g. "Удалить пользователя?". */
  title: ReactNode;
  /** Explanatory body — supports JSX so the caller can bold names. */
  description: ReactNode;
  onConfirm: () => void | Promise<void>;
  isPending?: boolean;
  confirmLabel?: string;
  cancelLabel?: string;

  /**
   * Controlled mode: pass both `open` and `onOpenChange`. Omit `trigger`.
   * On confirm success the dialog closes itself via `onOpenChange(false)`.
   */
  open?: boolean;
  onOpenChange?: (open: boolean) => void;

  /**
   * Uncontrolled mode: pass a `trigger` element. AlertDialog manages state
   * itself. Mutually exclusive with `open`/`onOpenChange`.
   */
  trigger?: ReactNode;
}

/**
 * Single source of truth for destructive confirmation dialogs. Supports both
 * controlled (open/onOpenChange) and uncontrolled (via trigger) usage.
 */
export function DeleteConfirmDialog({
  title,
  description,
  onConfirm,
  isPending = false,
  confirmLabel = "Удалить",
  cancelLabel = "Отмена",
  open,
  onOpenChange,
  trigger,
}: DeleteConfirmDialogProps) {
  const handleConfirm = async (event: React.MouseEvent) => {
    event.preventDefault();
    await onConfirm();
    onOpenChange?.(false);
  };

  const body = (
    <AlertDialogContent>
      <AlertDialogHeader>
        <AlertDialogTitle>{title}</AlertDialogTitle>
        <AlertDialogDescription>{description}</AlertDialogDescription>
      </AlertDialogHeader>
      <AlertDialogFooter>
        <AlertDialogCancel disabled={isPending}>{cancelLabel}</AlertDialogCancel>
        <AlertDialogAction
          onClick={handleConfirm}
          disabled={isPending}
          className="bg-destructive text-destructive-foreground hover:bg-destructive/90"
        >
          {isPending && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
          {confirmLabel}
        </AlertDialogAction>
      </AlertDialogFooter>
    </AlertDialogContent>
  );

  if (trigger) {
    return (
      <AlertDialog>
        <AlertDialogTrigger asChild>{trigger}</AlertDialogTrigger>
        {body}
      </AlertDialog>
    );
  }

  return (
    <AlertDialog open={open} onOpenChange={onOpenChange}>
      {body}
    </AlertDialog>
  );
}
