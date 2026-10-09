"use client";
import { Button } from "@/shared/ui/kit/button";
import { Input } from "@/shared/ui/kit/input";
import { Label } from "@/shared/ui/kit/label";
import dynamic from "next/dynamic";

const MarkdownEditor = dynamic(
  () => import("@/shared/ui/kit/markdown-editor").then((m) => ({ default: m.MarkdownEditor })),
  { ssr: false, loading: () => <div className="h-64 animate-pulse bg-muted rounded-md" /> },
);
import { Sheet, SheetContent, SheetFooter, SheetHeader, SheetTitle } from "@/shared/ui/kit/sheet";
import { Textarea } from "@/shared/ui/kit/textarea";
import { zodResolver } from "@hookform/resolvers/zod";
import { useState } from "react";
import { Controller, useForm } from "react-hook-form";
import { moduleSchema, type ModuleFormData } from "../model/schemas";

type Props = {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onSubmit: (data: {
    title: string;
    description: string | null;
    detailedDescription: string | null;
  }) => void;
  initialData: {
    title: string;
    description: string | null;
    detailedDescription: string | null;
  };
};

export function EditModuleDialog({
  open,
  onOpenChange,
  onSubmit: onSubmitProp,
  initialData,
}: Props) {
  const [editorViewMode, setEditorViewMode] = useState<"write" | "split" | "preview">("write");
  const {
    register,
    handleSubmit,
    control,
    formState: { errors },
  } = useForm<ModuleFormData>({
    resolver: zodResolver(moduleSchema),
    values: open
      ? {
          title: initialData.title,
          description: initialData.description ?? "",
          detailedDescription: initialData.detailedDescription ?? "",
        }
      : undefined,
  });

  // Закрытие управляется родителем — он сбрасывает editModuleData в null
  // сразу после submit, что размонтирует Sheet.
  const onSubmit = (data: ModuleFormData) => {
    onSubmitProp({
      title: data.title,
      description: data.description || null,
      detailedDescription: data.detailedDescription || null,
    });
  };

  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent
        className={
          editorViewMode === "split"
            ? "overflow-y-auto sm:max-w-[72vw]"
            : "overflow-y-auto sm:max-w-[600px]"
        }
      >
        <SheetHeader>
          <SheetTitle>Редактирование модуля</SheetTitle>
        </SheetHeader>

        <form onSubmit={handleSubmit(onSubmit)} className="flex flex-col gap-6 px-4">
          <div className="space-y-2">
            <Label htmlFor="edit-module-title">Название</Label>
            <Input
              id="edit-module-title"
              {...register("title")}
              placeholder="Введите название модуля"
            />
            {errors.title && <p className="text-sm text-destructive">{errors.title.message}</p>}
          </div>

          <div className="space-y-2">
            <Label htmlFor="edit-module-description">Краткое описание</Label>
            <Textarea
              id="edit-module-description"
              {...register("description")}
              placeholder="Краткое описание модуля"
              rows={3}
            />
            {errors.description && (
              <p className="text-sm text-destructive">{errors.description.message}</p>
            )}
          </div>

          <div className="space-y-2">
            <Label>Подробное описание</Label>
            <p className="text-xs text-muted-foreground">Поддерживается Markdown</p>
            <Controller
              control={control}
              name="detailedDescription"
              render={({ field }) => (
                <MarkdownEditor
                  value={field.value ?? ""}
                  onChange={field.onChange}
                  minHeight={200}
                  placeholder="Подробное описание модуля в формате Markdown..."
                  layout="split"
                  defaultViewMode="write"
                  onViewModeChange={setEditorViewMode}
                />
              )}
            />
            {errors.detailedDescription && (
              <p className="text-sm text-destructive">{errors.detailedDescription.message}</p>
            )}
          </div>

          <SheetFooter>
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
              Отмена
            </Button>
            <Button type="submit">Сохранить</Button>
          </SheetFooter>
        </form>
      </SheetContent>
    </Sheet>
  );
}
