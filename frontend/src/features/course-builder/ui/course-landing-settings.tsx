"use client";

import type { CourseBuilderDto, UpdateCourseRequest } from "@/entities/course";
import { routes } from "@/shared/config/routes";
import { useCourseSlug } from "@/shared/providers/course-id-provider";
import { PreviewUpload } from "@/shared/ui/components";
import { Icons, type IconComponent } from "@/shared/ui/icons";
import { Button } from "@/shared/ui/kit/button";
import { Card } from "@/shared/ui/kit/card";
import { Input } from "@/shared/ui/kit/input";
import { Label } from "@/shared/ui/kit/label";
import { zodResolver } from "@hookform/resolvers/zod";
import { ImagePlus } from "lucide-react";
import Link from "next/link";
import { useState, type KeyboardEvent } from "react";
import { useForm } from "react-hook-form";
import { z } from "zod";
import { useUpdateCourseSettings } from "../model/use-update-course-settings";
import { useUploadCoursePreview } from "../model/use-upload-course-preview";

const schema = z.object({
  slug: z
    .string()
    .min(2, "Минимум 2 символа")
    .max(100, "Максимум 100 символов")
    .regex(/^[a-z0-9](?:[a-z0-9-]*[a-z0-9])?$/, "Только строчные латинские буквы, цифры и дефисы"),
});

type FormData = z.infer<typeof schema>;

interface CourseLandingSettingsProps {
  courseId: string;
  course: CourseBuilderDto;
}

export function CourseLandingSettings({ courseId, course }: CourseLandingSettingsProps) {
  const courseSlug = useCourseSlug();
  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<FormData>({
    resolver: zodResolver(schema),
    values: {
      slug: course.slug,
    },
  });

  const mutation = useUpdateCourseSettings(courseId);
  const previewUpload = useUploadCoursePreview(courseId);

  const [learningOutcomes, setLearningOutcomes] = useState<string[]>(course.learningOutcomes ?? []);
  const [targetAudience, setTargetAudience] = useState<string[]>(course.targetAudience ?? []);
  const [prerequisites, setPrerequisites] = useState<string[]>(course.prerequisites ?? []);

  const onSubmit = (data: FormData) => {
    // PATCH: шлём только то, что меняется.
    // previewId меняем только если пользователь загрузил новый или удалил текущий —
    // в остальных случаях бэк сохранит существующее значение нетронутым.
    const request: UpdateCourseRequest = {};
    if (data.slug !== course.slug) request.slug = data.slug;
    if (previewUpload.uploadedAssetId) request.previewId = previewUpload.uploadedAssetId;
    else if (previewUpload.isDeleted) request.previewId = null;

    // Лендинг-копию редактирует только эта форма — отправляем текущее состояние списков
    // всегда (PATCH идемпотентен; бэк нормализует: trim, без пустых, с лимитами).
    request.learningOutcomes = learningOutcomes;
    request.targetAudience = targetAudience;
    request.prerequisites = prerequisites;

    mutation.mutate(request);
  };

  return (
    <form onSubmit={handleSubmit(onSubmit)}>
      <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between mb-4">
        <div className="min-w-0">
          <h2 className="text-lg font-semibold">Лендинг курса</h2>
          <p className="text-sm text-muted-foreground">
            Настройте публичную страницу курса для привлечения студентов
          </p>
        </div>
        {/* #498: предпросмотр — внутренняя страница, открываем в той же вкладке. */}
        <Button variant="outline" size="sm" className="shrink-0 self-start sm:self-auto" asChild>
          <Link href={routes.courseOverview(courseSlug)}>
            <Icons.view size={14} />
            Предпросмотр
          </Link>
        </Button>
      </div>

      <Card className="p-6 space-y-3 mb-4">
        <div>
          <Label htmlFor="slug">URL-slug</Label>
          <Input id="slug" {...register("slug")} placeholder="my-course" className="mt-1" />
          <p className="text-xs text-muted-foreground mt-1">Адрес курса: /courses/{"{slug}"}</p>
          {errors.slug && <p className="text-sm text-destructive mt-1">{errors.slug.message}</p>}
        </div>
      </Card>

      <Card className="p-6 space-y-3 mb-4">
        <div className="flex items-center gap-2">
          <ImagePlus size={15} className="text-muted-foreground" />
          <Label>Обложка курса</Label>
        </div>
        <PreviewUpload
          hook={previewUpload}
          imageId={course.imageId}
          initialPreviewUrl={course.imageId ? `/api/files/${course.imageId}/content` : null}
          alt="Обложка курса"
          aspectRatio="video"
        />
        <p className="text-xs text-muted-foreground">
          Рекомендуемый размер: 1920x1080 (16:9). Отображается в каталоге и на странице курса.
        </p>
      </Card>

      <div className="space-y-4">
        <div>
          <h3 className="text-base font-semibold">Что даёт курс</h3>
          <p className="text-sm text-muted-foreground">
            Эти блоки видят студенты, которые ещё не купили курс — помогают понять ценность.
          </p>
        </div>

        <BulletListEditor
          title="Чему вы научитесь"
          description="Конкретные навыки и результаты после прохождения курса."
          placeholder="Например: Соберёте production-ready API на .NET"
          icon={Icons.graduation}
          items={learningOutcomes}
          onChange={setLearningOutcomes}
        />

        <BulletListEditor
          title="Для кого этот курс"
          description="Кому подойдёт — уровень, роль, цели."
          placeholder="Например: Junior-разработчикам, желающим вырасти до middle"
          icon={Icons.target}
          items={targetAudience}
          onChange={setTargetAudience}
        />

        <BulletListEditor
          title="Что нужно знать заранее"
          description="Базовые требования и пререквизиты."
          placeholder="Например: Базовый C# и основы ООП"
          icon={Icons.listChecks}
          items={prerequisites}
          onChange={setPrerequisites}
        />
      </div>

      <div className="mt-4">
        <Button type="submit" disabled={mutation.isPending}>
          {mutation.isPending ? "Сохранение..." : "Сохранить"}
        </Button>
      </div>
    </form>
  );
}

interface BulletListEditorProps {
  title: string;
  description?: string;
  placeholder: string;
  icon?: IconComponent;
  items: string[];
  onChange: (next: string[]) => void;
}

function BulletListEditor({
  title,
  description,
  placeholder,
  icon: Icon,
  items,
  onChange,
}: BulletListEditorProps) {
  const [draft, setDraft] = useState("");

  const addItem = () => {
    const trimmed = draft.trim();
    if (!trimmed) return;
    onChange([...items, trimmed]);
    setDraft("");
  };

  const handleKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    if (event.key === "Enter") {
      event.preventDefault();
      addItem();
    }
  };

  return (
    <Card className="p-6 space-y-3">
      <div>
        <div className="flex items-center gap-2">
          {Icon && <Icon size={15} className="text-muted-foreground" />}
          <Label>{title}</Label>
        </div>
        {description && <p className="text-xs text-muted-foreground mt-1">{description}</p>}
      </div>

      {items.length > 0 && (
        <ul className="space-y-2">
          {items.map((item, index) => (
            <li
              key={`${index}-${item}`}
              className="flex items-start gap-2 rounded-md border bg-muted/30 px-3 py-2"
            >
              <Icons.check className="mt-0.5 size-4 shrink-0 text-primary" />
              <span className="flex-1 text-sm">{item}</span>
              <button
                type="button"
                onClick={() => onChange(items.filter((_, i) => i !== index))}
                className="shrink-0 text-muted-foreground transition-colors hover:text-destructive"
                aria-label="Удалить пункт"
              >
                <Icons.close size={15} />
              </button>
            </li>
          ))}
        </ul>
      )}

      <div className="flex gap-2">
        <Input
          value={draft}
          onChange={(event) => setDraft(event.target.value)}
          onKeyDown={handleKeyDown}
          placeholder={placeholder}
        />
        <Button type="button" variant="outline" onClick={addItem} disabled={!draft.trim()}>
          <Icons.add size={15} />
          Добавить
        </Button>
      </div>
    </Card>
  );
}
