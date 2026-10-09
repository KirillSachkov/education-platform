"use client";
import { Input } from "@/shared/ui/kit/input";
import { Label } from "@/shared/ui/kit/label";
import { Textarea } from "@/shared/ui/kit/textarea";
import { FormDialog } from "@/shared/ui/components";
import { zodResolver } from "@hookform/resolvers/zod";
import { useForm } from "react-hook-form";
import { courseFormSchema, type CourseFormData } from "../model/schemas";
import { useCreateCourse } from "../model/use-create-course";

type Props = {
  open: boolean;
  onOpenChange: (open: boolean) => void;
};

const defaultValues: CourseFormData = {
  title: "",
  description: "",
  slug: "",
  kind: "COURSE",
  // Создание не управляет флагом — backend ставит default true (Course.ShowInFullAccess).
  // Автор снимает галочку позже через «Редактировать» (#418).
  showInFullAccess: true,
};

const KIND_OPTIONS: { value: CourseFormData["kind"]; label: string; hint: string }[] = [
  { value: "COURSE", label: "Курс", hint: "Модули + материалы + задания" },
  { value: "INTENSIVE", label: "Интенсив", hint: "Только модули и материалы" },
  { value: "MARATHON", label: "Марафон", hint: "С дедлайнами, без заданий" },
];

export function CreateCourseDialog({ open, onOpenChange }: Props) {
  const { createCourse } = useCreateCourse();

  const {
    register,
    handleSubmit,
    reset,
    watch,
    setValue,
    formState: { errors },
  } = useForm<CourseFormData>({
    resolver: zodResolver(courseFormSchema),
    defaultValues,
  });

  const selectedKind = watch("kind");

  const handleClose = () => {
    reset(defaultValues);
    onOpenChange(false);
  };

  const onSubmit = (data: CourseFormData) => {
    handleClose();
    void (async () => {
      await createCourse({
        title: data.title,
        description: data.description,
        slug: data.slug,
        kind: data.kind,
      }).catch(() => undefined);
    })();
  };

  return (
    <FormDialog
      open={open}
      onOpenChange={onOpenChange}
      title="Создание курса"
      description="Заполните форму для создания нового курса"
      onSubmit={handleSubmit(onSubmit)}
      onCancel={handleClose}
      submitLabel="Создать"
    >
      <div className="space-y-2">
        <Label>Тип</Label>
        <div className="grid grid-cols-3 gap-2">
          {KIND_OPTIONS.map((option) => (
            <button
              key={option.value}
              type="button"
              onClick={() => setValue("kind", option.value)}
              className={`rounded-md border px-3 py-2 text-left text-sm transition-colors ${
                selectedKind === option.value
                  ? "border-primary bg-primary/10 text-primary"
                  : "border-input hover:bg-accent"
              }`}
            >
              {option.label}
              <span className="block text-xs text-muted-foreground mt-0.5">{option.hint}</span>
            </button>
          ))}
        </div>
      </div>

      <div className="space-y-2">
        <Label htmlFor="title">Название</Label>
        <Input id="title" {...register("title")} placeholder="Введите название курса" />
        {errors.title && <p className="text-sm text-destructive">{errors.title.message}</p>}
      </div>

      <div className="space-y-2">
        <Label htmlFor="description">Описание</Label>
        <Textarea
          id="description"
          {...register("description")}
          placeholder="Краткое описание курса"
          rows={3}
        />
        {errors.description && (
          <p className="text-sm text-destructive">{errors.description.message}</p>
        )}
      </div>

      <div className="space-y-2">
        <Label htmlFor="slug">URL-slug</Label>
        <Input id="slug" {...register("slug")} placeholder="my-course" />
        <p className="text-xs text-muted-foreground">
          Латинские буквы, цифры и дефисы. Будет в URL: /courses/
          {"{slug}"}
        </p>
        {errors.slug && <p className="text-sm text-destructive">{errors.slug.message}</p>}
      </div>
    </FormDialog>
  );
}
