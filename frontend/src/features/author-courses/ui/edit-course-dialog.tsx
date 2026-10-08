"use client";

import type { CourseSummaryDto } from "@/entities/course";
import { Button } from "@/shared/ui/kit/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/shared/ui/kit/dialog";
import { Input } from "@/shared/ui/kit/input";
import { Label } from "@/shared/ui/kit/label";
import { Switch } from "@/shared/ui/kit/switch";
import { Textarea } from "@/shared/ui/kit/textarea";
import { zodResolver } from "@hookform/resolvers/zod";
import { Controller, useForm } from "react-hook-form";
import { courseFormSchema, type CourseFormData } from "../model/schemas";
import { useUpdateCourse } from "../model/use-update-course";

type Props = {
  course: CourseSummaryDto;
  open: boolean;
  onOpenChange: (open: boolean) => void;
};

export function EditCourseDialog({ course, open, onOpenChange }: Props) {
  const { updateCourse } = useUpdateCourse();

  // `/courses/my` отдаёт фактический showInFullAccess — гидратируем им напрямую.
  // Шлём флаг в PATCH только если он изменился относительно гидратированного значения.
  const initialShowInFullAccess = course.showInFullAccess;

  const {
    register,
    handleSubmit,
    control,
    formState: { errors },
  } = useForm<CourseFormData>({
    resolver: zodResolver(courseFormSchema),
    values: {
      title: course.title,
      description: course.description,
      slug: course.slug,
      kind: course.kind,
      showInFullAccess: initialShowInFullAccess,
    },
  });

  // Fire-and-forget: закрываем форму сразу, тосты из useUpdateCourse.
  const onSubmit = (data: CourseFormData) => {
    void updateCourse({
      courseId: course.id,
      request: {
        title: data.title,
        description: data.description,
        slug: data.slug !== course.slug ? data.slug : undefined,
        showInFullAccess:
          data.showInFullAccess !== initialShowInFullAccess ? data.showInFullAccess : undefined,
      },
    });
    onOpenChange(false);
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-[500px]">
        <DialogHeader>
          <DialogTitle>Редактирование курса</DialogTitle>
          <DialogDescription>Измените данные курса</DialogDescription>
        </DialogHeader>

        <form onSubmit={handleSubmit(onSubmit)}>
          <div className="space-y-4 py-4">
            <div className="space-y-2">
              <Label htmlFor="edit-title">Название</Label>
              <Input id="edit-title" {...register("title")} placeholder="Введите название курса" />
              {errors.title && <p className="text-sm text-destructive">{errors.title.message}</p>}
            </div>

            <div className="space-y-2">
              <Label htmlFor="edit-description">Описание</Label>
              <Textarea
                id="edit-description"
                {...register("description")}
                placeholder="Краткое описание курса"
                rows={3}
              />
              {errors.description && (
                <p className="text-sm text-destructive">{errors.description.message}</p>
              )}
            </div>

            <div className="space-y-2">
              <Label htmlFor="edit-slug">URL-slug</Label>
              <Input id="edit-slug" {...register("slug")} placeholder="my-course" />
              <p className="text-xs text-muted-foreground">Латинские буквы, цифры и дефисы</p>
              {errors.slug && <p className="text-sm text-destructive">{errors.slug.message}</p>}
            </div>

            <div className="flex items-start justify-between gap-4 rounded-lg border border-border/60 p-3">
              <div>
                <Label htmlFor="edit-show-in-full-access">Входит в полный доступ</Label>
                <p className="mt-1 text-xs text-muted-foreground">
                  Показывать курс в витрине «Полный доступ» на странице тарифов. Снимите для
                  интенсивов, которые и так включены внутри других курсов.
                </p>
              </div>
              <Controller
                name="showInFullAccess"
                control={control}
                render={({ field }) => (
                  <Switch
                    id="edit-show-in-full-access"
                    checked={field.value}
                    onCheckedChange={field.onChange}
                  />
                )}
              />
            </div>
          </div>

          <DialogFooter>
            <Button
              type="button"
              variant="outline"
              onClick={() => onOpenChange(false)}
            >
              Отмена
            </Button>
            <Button type="submit">Сохранить</Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
