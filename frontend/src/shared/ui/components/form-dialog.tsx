"use client";

import { cn } from "@/shared/lib/css";
import { Button } from "@/shared/ui/kit/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/shared/ui/kit/dialog";
import { Loader2 } from "lucide-react";
import type { FormEvent, ReactNode } from "react";

interface FormDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  title: ReactNode;
  description?: ReactNode;
  /**
   * Wire this to `handleSubmit(onSubmit)` returned by react-hook-form.
   * The dialog renders a `<form>` element and forwards the event.
   */
  onSubmit: (event: FormEvent<HTMLFormElement>) => void | Promise<void>;
  /** Called when Cancel is clicked. Defaults to `onOpenChange(false)`. */
  onCancel?: () => void;
  /** Shows spinner + disables both buttons. */
  isPending?: boolean;
  submitLabel?: string;
  cancelLabel?: string;
  /**
   * Disables the submit button regardless of pending state. Useful for form
   * validation gating (e.g. `!formState.isValid`).
   */
  submitDisabled?: boolean;
  /** Variant for the primary action (e.g. "destructive" for delete forms). */
  submitVariant?:
    | "default"
    | "destructive"
    | "outline"
    | "secondary"
    | "ghost"
    | "link";
  /** Max width of the dialog body. Defaults to `sm:max-w-[500px]`. */
  contentClassName?: string;
  /** Extra wrapper class for the form fields area. */
  bodyClassName?: string;
  children: ReactNode;
}

/**
 * Unified shell for "create/edit entity" dialogs: title + description + form
 * body + Cancel/Submit footer. Callers own the form state (react-hook-form)
 * and pass a prepared `onSubmit` handler.
 */
export function FormDialog({
  open,
  onOpenChange,
  title,
  description,
  onSubmit,
  onCancel,
  isPending = false,
  submitLabel = "Сохранить",
  cancelLabel = "Отмена",
  submitDisabled = false,
  submitVariant = "default",
  contentClassName,
  bodyClassName,
  children,
}: FormDialogProps) {
  const handleCancel = () => {
    if (onCancel) {
      onCancel();
      return;
    }
    onOpenChange(false);
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className={cn("sm:max-w-[500px]", contentClassName)}>
        <DialogHeader>
          <DialogTitle>{title}</DialogTitle>
          {description && <DialogDescription>{description}</DialogDescription>}
        </DialogHeader>

        <form onSubmit={onSubmit}>
          <div className={cn("space-y-4 py-4", bodyClassName)}>{children}</div>

          <DialogFooter>
            <Button
              type="button"
              variant="outline"
              onClick={handleCancel}
              disabled={isPending}
            >
              {cancelLabel}
            </Button>
            <Button
              type="submit"
              variant={submitVariant}
              disabled={isPending || submitDisabled}
            >
              {isPending && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
              {submitLabel}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
