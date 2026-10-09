"use client";

import { useMarkdownFileUpload, useMarkdownImageUpload } from "@/entities/file";
import { Button } from "@/shared/ui/kit/button";
import { Input } from "@/shared/ui/kit/input";
import { Label } from "@/shared/ui/kit/label";
import dynamic from "next/dynamic";

const MarkdownEditor = dynamic(
  () => import("@/shared/ui/kit/markdown-editor").then((m) => ({ default: m.MarkdownEditor })),
  { ssr: false, loading: () => <div className="h-64 animate-pulse bg-muted rounded-md" /> },
);
import { Sheet, SheetContent, SheetDescription, SheetTitle } from "@/shared/ui/kit/sheet";
import { zodResolver } from "@hookform/resolvers/zod";
import { ENTITY_ICONS } from "@/shared/config/entity-icons";
import { useState } from "react";
import { Controller, useForm } from "react-hook-form";
import { issueSchema, issueDefaultValues, type IssueFormData } from "../model/schemas";

type Props = {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  projectName: string;
  onSubmit: (data: { title: string; content: string; draftId: string }) => void;
};

export function CreateIssueSheet({
  open,
  onOpenChange,
  projectName,
  onSubmit: onSubmitProp,
}: Props) {
  // Свежий draftId на каждое открытие. Сбрасываем в `handleClose` (когда лист
  // закрывается, готовимся к следующему открытию) — без useEffect на проп `open`.
  const [draftId, setDraftId] = useState(() => crypto.randomUUID());

  const { handleImagePaste } = useMarkdownImageUpload({ draftId });
  const { handleFileAttach } = useMarkdownFileUpload({ draftId });

  const {
    register,
    handleSubmit,
    control,
    reset,
    formState: { errors },
  } = useForm<IssueFormData>({
    resolver: zodResolver(issueSchema),
    defaultValues: issueDefaultValues,
  });
  const [editorViewMode, setEditorViewMode] = useState<"write" | "split" | "preview">("write");

  const handleClose = () => {
    reset(issueDefaultValues);
    setDraftId(crypto.randomUUID());
    onOpenChange(false);
  };

  const onSubmit = (data: IssueFormData) => {
    onSubmitProp({
      title: data.title,
      content: data.content ?? "",
      draftId,
    });
    handleClose();
  };

  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent
        side="right"
        size="wide"
        showCloseButton={false}
        className={
          editorViewMode === "split" ? "p-0 gap-0 sm:max-w-[72vw]" : "p-0 gap-0 sm:max-w-[56vw]"
        }
      >
        <form onSubmit={handleSubmit(onSubmit)} className="flex flex-col h-full">
          {/* Header */}
          <div className="border-b border-border/50 px-6 py-4 shrink-0">
            <div className="flex items-center justify-between">
              <div className="flex items-center gap-3 min-w-0">
                <div className="size-9 rounded-xl bg-orange/10 flex items-center justify-center shrink-0">
                  <ENTITY_ICONS.issue size={16} className="text-orange" />
                </div>
                <div className="min-w-0">
                  <SheetTitle className="text-base leading-tight">Новая задача</SheetTitle>
                  <SheetDescription className="text-xs text-muted-foreground truncate mt-0.5">
                    {projectName}
                  </SheetDescription>
                </div>
              </div>
              <div className="flex items-center gap-2 shrink-0">
                <Button type="button" variant="ghost" size="sm" onClick={handleClose}>
                  Отмена
                </Button>
                <Button
                  type="submit"
                  size="sm"
                  className="bg-orange text-primary-foreground hover:bg-orange/90"
                >
                  Создать задачу
                </Button>
              </div>
            </div>
          </div>

          {/* Title field */}
          <div className="shrink-0 px-6 pt-5 pb-4 border-b border-border/50">
            <div className="space-y-1.5">
              <Label htmlFor="issue-title" className="text-sm font-medium">
                Название задачи
              </Label>
              <Input
                id="issue-title"
                {...register("title")}
                placeholder="Введите название задачи"
                className="h-10"
              />
              {errors.title && <p className="text-sm text-destructive">{errors.title.message}</p>}
            </div>
          </div>

          {/* Editor — fills remaining height */}
          <div className="flex-1 flex flex-col min-h-0 px-6 pt-4 pb-6">
            <Label className="text-sm font-medium mb-1.5 shrink-0">Описание задачи</Label>
            <Controller
              name="content"
              control={control}
              render={({ field }) => (
                <MarkdownEditor
                  value={field.value ?? ""}
                  onChange={field.onChange}
                  onImagePaste={handleImagePaste}
                  onFileAttach={handleFileAttach}
                  placeholder="Опишите задачу, критерии оценки, примеры..."
                  className="flex-1 min-h-0"
                  layout="split"
                  defaultViewMode="write"
                  onViewModeChange={setEditorViewMode}
                />
              )}
            />
          </div>
        </form>
      </SheetContent>
    </Sheet>
  );
}
