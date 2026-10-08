"use client";

import type { CourseBuilderDto } from "@/entities/course";
import { useToggleIsNew } from "@/entities/course";
import { TagsField } from "@/entities/tag";
import { EntityTypes } from "@/shared/config/entity-types";
import { Button } from "@/shared/ui/kit/button";
import { Card } from "@/shared/ui/kit/card";
import { Input } from "@/shared/ui/kit/input";
import { Label } from "@/shared/ui/kit/label";
import { Textarea } from "@/shared/ui/kit/textarea";
import { Switch } from "@/shared/ui/kit/switch";
import { zodResolver } from "@hookform/resolvers/zod";
import { useForm } from "react-hook-form";
import { z } from "zod";
import { useUpdateCourseSettings } from "../model/use-update-course-settings";

const schema = z.object({
  title: z.string().min(1, "Название обязательно").max(200, "Максимум 200 символов"),
  description: z.string().max(2000, "Максимум 2000 символов"),
});

type FormData = z.infer<typeof schema>;

interface CourseSettingsProps {
  courseId: string;
  course: CourseBuilderDto;
}

export function CourseSettings({ courseId, course }: CourseSettingsProps) {
  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<FormData>({
    resolver: zodResolver(schema),
    values: {
      title: course.title,
      description: course.description,
    },
  });

  const mutation = useUpdateCourseSettings(courseId);
  const toggleIsNew = useToggleIsNew(courseId);

  const onSubmit = (data: FormData) => {
    mutation.mutate({
      title: data.title,
      description: data.description,
    });
  };

  return (
    <form onSubmit={handleSubmit(onSubmit)}>
      <Card className="p-6 mt-4 space-y-5">
        <div className="space-y-2">
          <Label htmlFor="course-title">Название курса</Label>
          <Input id="course-title" {...register("title")} placeholder="Введите название курса" />
          {errors.title && <p className="text-sm text-destructive">{errors.title.message}</p>}
        </div>

        <div className="space-y-2">
          <Label htmlFor="course-description">Описание</Label>
          <Textarea
            id="course-description"
            {...register("description")}
            placeholder="Краткое описание курса"
            rows={4}
          />
          {errors.description && (
            <p className="text-sm text-destructive">{errors.description.message}</p>
          )}
        </div>
      </Card>

      <Card className="p-6 mt-4">
        <div className="flex items-center justify-between gap-4">
          <div>
            <Label htmlFor="is-new-toggle">Новый курс</Label>
            <p className="text-xs text-muted-foreground mt-1">
              Показывать бейдж «Новый» в каталоге
            </p>
          </div>
          <Switch
            id="is-new-toggle"
            checked={course.isNew}
            onCheckedChange={(checked) => toggleIsNew.mutate(checked)}
            disabled={toggleIsNew.isPending}
          />
        </div>
      </Card>

      <Card className="p-6 mt-4 space-y-4">
        <div>
          <h3 className="text-sm font-medium">Теги курса</h3>
          <p className="text-xs text-muted-foreground mt-1">
            Используются в каталоге и привязке контента
          </p>
        </div>
        <TagsField key={courseId} entityId={courseId} entityType={EntityTypes.COURSE} />
      </Card>

      <div className="mt-4">
        <Button type="submit" disabled={mutation.isPending}>
          {mutation.isPending ? "Сохранение..." : "Сохранить"}
        </Button>
      </div>
    </form>
  );
}
