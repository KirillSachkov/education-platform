"use client";

import type { CourseId } from "@/entities/course";
import { Button } from "@/shared/ui/kit/button";
import { DeleteConfirmDialog } from "@/shared/ui/components";
import { Trash2 } from "lucide-react";
import { useDeleteCourse } from "../model/use-delete-course";

interface DeleteCourseDialogProps {
  courseId: CourseId;
}

export function DeleteCourseDialog({ courseId }: DeleteCourseDialogProps) {
  const { deleteCourse, isPending } = useDeleteCourse();

  return (
    <DeleteConfirmDialog
      title="Удалить курс?"
      description="Это действие нельзя отменить. Курс будет удалён навсегда."
      isPending={isPending}
      onConfirm={() => deleteCourse(courseId)}
      trigger={
        <Button
          variant="ghost"
          size="icon"
          className="text-xs text-destructive hover:text-destructive sm:size-auto sm:px-3 sm:py-1.5"
        >
          <Trash2 size={13} /> <span className="hidden sm:inline">Удалить</span>
        </Button>
      }
    />
  );
}
