"use client";

import { useMarkdownFileUpload, useMarkdownImageUpload } from "@/entities/file";
import { TagsField } from "@/entities/tag";
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
import dynamic from "next/dynamic";
import { useState } from "react";
import { Controller, useForm } from "react-hook-form";
import { moduleSchema, type ModuleFormData } from "../model/schemas";

const MarkdownEditor = dynamic(
  () => import("@/shared/ui/kit/markdown-editor").then((m) => ({ default: m.MarkdownEditor })),
  { ssr: false, loading: () => <div className="h-64 animate-pulse bg-muted rounded-md" /> },
);

const defaultValues: ModuleFormData = { title: "", description: "", detailedDescription: "" };

export type CreateProjectSubmitData = {
  title: string;
  description: string;
  detailedDescription: string;
  tags: string[];
  draftId: string;
  requiresGithubConnection: boolean;
  requiresReviewApp: boolean;
  isAutoReviewEnabled: boolean;
};

type Props = {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onSubmit: (data: CreateProjectSubmitData) => void | Promise<unknown>;
};

export function CreateProjectDialog({ open, onOpenChange, onSubmit: onSubmitProp }: Props) {
  const [tags, setTags] = useState<string[]>([]);
  const [reviewSettings, setReviewSettings] = useState({
    requiresGithubConnection: true,
    requiresReviewApp: true,
    isAutoReviewEnabled: true,
  });

  // Свежий draftId на каждое открытие — сбрасываем в handleClose, без useEffect на `open`.
  const [draftId, setDraftId] = useState(() => crypto.randomUUID());
  const { handleImagePaste } = useMarkdownImageUpload({ draftId });
  const { handleFileAttach } = useMarkdownFileUpload({ draftId });

  const {
    register,
    handleSubmit,
    control,
    reset,
    formState: { errors },
  } = useForm<ModuleFormData>({
    resolver: zodResolver(moduleSchema),
    defaultValues,
  });

  const handleClose = () => {
    reset(defaultValues);
    setTags([]);
    setReviewSettings({
      requiresGithubConnection: true,
      requiresReviewApp: true,
      isAutoReviewEnabled: true,
    });
    setDraftId(crypto.randomUUID());
    onOpenChange(false);
  };

  const updateReviewSettings = (next: Partial<typeof reviewSettings>) => {
    setReviewSettings((current) => {
      const merged = { ...current, ...next };
      if (next.isAutoReviewEnabled === true) {
        merged.requiresGithubConnection = true;
        merged.requiresReviewApp = true;
      }
      if (!merged.requiresGithubConnection || !merged.requiresReviewApp) {
        merged.isAutoReviewEnabled = false;
      }
      return merged;
    });
  };

  // Fire-and-forget: закрываем форму сразу; create → bind ассетов/тегов идут в фоне (см. host).
  const onSubmit = (data: ModuleFormData) => {
    onSubmitProp({
      title: data.title,
      description: data.description ?? "",
      detailedDescription: data.detailedDescription ?? "",
      tags,
      draftId,
      ...reviewSettings,
    });
    handleClose();
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-[760px] max-h-[90vh] overflow-y-auto">
        <DialogHeader>
          <DialogTitle>Создание проекта</DialogTitle>
          <DialogDescription>
            Проект — набор практических задач. Опишите, что предстоит сделать ученику.
          </DialogDescription>
        </DialogHeader>

        <form onSubmit={handleSubmit(onSubmit)}>
          <div className="space-y-4 py-4">
            <div className="space-y-2">
              <Label htmlFor="project-title">Название</Label>
              <Input
                id="project-title"
                {...register("title")}
                placeholder="Введите название проекта"
              />
              {errors.title && <p className="text-sm text-destructive">{errors.title.message}</p>}
            </div>

            <div className="space-y-2">
              <Label htmlFor="project-description">Краткая аннотация</Label>
              <Textarea
                id="project-description"
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
                Что предстоит сделать, цели и контекст. Ученик увидит это на странице заданий —
                отдельная вводная задача больше не нужна.
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

            <div className="space-y-2">
              <Label>Теги</Label>
              <TagsField value={tags} onChange={setTags} />
            </div>

            <div className="space-y-3 rounded-lg border border-border/50 p-4">
              <div>
                <Label className="text-sm font-medium">Проверка задач</Label>
                <p className="text-xs text-muted-foreground mt-1">
                  Настройки применяются ко всем задачам проекта по умолчанию.
                </p>
              </div>

              <div className="flex items-center justify-between gap-3">
                <Label htmlFor="project-requires-github" className="text-sm">
                  Требовать привязку GitHub
                </Label>
                <Switch
                  id="project-requires-github"
                  checked={reviewSettings.requiresGithubConnection}
                  onCheckedChange={(value) =>
                    updateReviewSettings({ requiresGithubConnection: value })
                  }
                />
              </div>

              <div className="flex items-center justify-between gap-3">
                <Label htmlFor="project-requires-app" className="text-sm">
                  Требовать GitHub App / review bot
                </Label>
                <Switch
                  id="project-requires-app"
                  checked={reviewSettings.requiresReviewApp}
                  onCheckedChange={(value) => updateReviewSettings({ requiresReviewApp: value })}
                />
              </div>

              <div className="flex items-center justify-between gap-3">
                <Label htmlFor="project-auto-review" className="text-sm">
                  Включить AI-проверку
                </Label>
                <Switch
                  id="project-auto-review"
                  checked={reviewSettings.isAutoReviewEnabled}
                  onCheckedChange={(value) =>
                    updateReviewSettings({ isAutoReviewEnabled: value })
                  }
                />
              </div>
            </div>
          </div>

          <DialogFooter>
            <Button type="button" variant="outline" onClick={handleClose}>
              Отмена
            </Button>
            <Button type="submit">Создать</Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
