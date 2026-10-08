"use client";

import type { MediaStatus } from "@/shared/types";
import {
  AlertDialog,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from "@/shared/ui/kit/alert-dialog";
import { Button } from "@/shared/ui/kit/button";

export type VideoManageDialogProps = {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onConfirm: (deleteFromProvider: boolean) => void;
  isDeleting?: boolean;
  videoStatus?: MediaStatus;
};

const DIALOG_TEXT: Record<
  "ready" | "processing" | "failed",
  { title: string; description: string }
> = {
  ready: {
    title: "Удалить видео?",
    description:
      "Выберите способ удаления видео. Отвязка убирает видео только из системы, а полное удаление — также из Kinescope.",
  },
  processing: {
    title: "Прервать обработку?",
    description:
      "Видео ещё обрабатывается. Вы можете прервать обработку и удалить видео, чтобы загрузить новое.",
  },
  failed: {
    title: "Удалить видео?",
    description:
      "Обработка видео завершилась с ошибкой. Удалите видео, чтобы попробовать загрузить снова.",
  },
};

function getDialogText(status?: MediaStatus) {
  if (status === "failed") return DIALOG_TEXT.failed;
  if (status === "ready") return DIALOG_TEXT.ready;
  return DIALOG_TEXT.processing;
}

export function VideoManageDialog({
  open,
  onOpenChange,
  onConfirm,
  isDeleting = false,
  videoStatus,
}: VideoManageDialogProps) {
  const { title, description } = getDialogText(videoStatus);

  return (
    <AlertDialog open={open} onOpenChange={onOpenChange}>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{title}</AlertDialogTitle>
          <AlertDialogDescription>{description}</AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel disabled={isDeleting}>Отмена</AlertDialogCancel>
          <Button
            variant="outline"
            onClick={() => onConfirm(false)}
            disabled={isDeleting}
          >
            {isDeleting ? "Удаление..." : "Отвязать"}
          </Button>
          <Button
            variant="destructive"
            onClick={() => onConfirm(true)}
            disabled={isDeleting}
          >
            {isDeleting ? "Удаление..." : "Удалить полностью"}
          </Button>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}
