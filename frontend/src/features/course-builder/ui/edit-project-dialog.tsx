"use client";

import { useMarkdownFileUpload, useMarkdownImageUpload } from "@/entities/file";
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
import { Textarea } from "@/shared/ui/kit/textarea";
import { zodResolver } from "@hookform/resolvers/zod";
import dynamic from "next/dynamic";
import { Controller, useForm } from "react-hook-form";
import { moduleSchema, type ModuleFormData } from "../model/schemas";

const MarkdownEditor = dynamic(
  () => import("@/shared/ui/kit/markdown-editor").then((m) => ({ default: m.MarkdownEditor })),
  { ssr: false, loading: () => <div className="h-64 animate-pulse bg-muted rounded-md" /> },
);

export type EditProjectSubmitData = {
  title: string;
  description: string;
  detailedDescription: string;
};

type Props = {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  projectId: string;
  onSubmit: (data: EditProjectSubmitData) => void;
  initialData: { title: string; description: string; detailedDescription: string };
};

export function EditProjectDialog({
  open,
  onOpenChange,
  projectId,
  onSubmit: onSubmitProp,
  initialData,
}: Props) {
  // Edit-mode: ассеты привязываются к проекту сразу при вставке; sync на сохранении
  // (host) подчищает осиротевшие.
  const { handleImagePaste } = useMarkdownImageUpload({
    targetEntity: { type: "project", id: projectId },
  });
  const { handleFileAttach } = useMarkdownFileUpload({
    targetEntity: { type: "project", id: projectId },
  });

  const {
    register,
    handleSubmit,
    control,
    formState: { errors },
  } = useForm<ModuleFormData>({
    resolver: zodResolver(moduleSchema),
    values: open ? initialData : undefined,
  });

  const handleClose = () => {
    onOpenChange(false);
  };

  // Закрытие управляется родителем — он сбрасывает editProjectData в null
  // сразу после submit, что размонтирует Dialog.
  const onSubmit = (data: ModuleFormData) => {
    onSubmitProp({
      title: data.title,
      description: data.description ?? "",
      detailedDescription: data.detailedDescription ?? "",
    });
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-[760px] max-h-[90vh] overflow-y-auto">
        <DialogHeader>
          <DialogTitle>Редактирование проекта</DialogTitle>
          <DialogDescription>Измените данные проекта</DialogDescription>
        </DialogHeader>

        <form onSubmit={handleSubmit(onSubmit)}>
          <div className="space-y-4 py-4">
            <div className="space-y-2">
              <Label htmlFor="edit-project-title">Название</Label>
              <Input
                id="edit-project-title"
                {...register("title")}
                placeholder="Введите название проекта"
              />
              {errors.title && <p className="text-sm text-destructive">{errors.title.message}</p>}
            </div>

            <div className="space-y-2">
              <Label htmlFor="edit-project-description">Краткая аннотация</Label>
              <Textarea
                id="edit-project-description"
                {...register("description")}
                placeholder="Одна-две строки для карточки в списке (необязательно)"
                rows={2}
              />
              {errors.description && (
                <p className="text-sm text-destructive">{errors.description.message}</p>
              )}
            </div>

            <div className="space-y-1.5">
              <Label>Описание проекта для ученика</Label>
              <p className="text-xs text-muted-foreground">
                Что предстоит сделать, цели и контекст. Ученик увидит это на странице заданий.
              </p>
              <Controller
                name="detailedDescription"
                control={control}
                render={({ field }) => (
                  <MarkdownEditor
                    value={field.value ?? ""}
                    onChange={field.onChange}
                    onImagePaste={handleImagePaste}
                    onFileAttach={handleFileAttach}
                    placeholder="## Что предстоит сделать&#10;&#10;Опишите проект, цели и ожидаемый результат. Можно вставлять картинки и файлы."
                    minHeight={280}
                    layout="split"
                    defaultViewMode="write"
                  />
                )}
              />
              {errors.detailedDescription && (
                <p className="text-sm text-destructive">{errors.detailedDescription.message}</p>
              )}
            </div>
          </div>

          <DialogFooter>
            <Button type="button" variant="outline" onClick={handleClose}>
              Отмена
            </Button>
            <Button type="submit">Сохранить</Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
