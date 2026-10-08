"use client";

import { projectReviewContextQueryOptions } from "@/entities/project";
import type { ProjectReviewContextDto } from "@/entities/project";
import { Button } from "@/shared/ui/kit/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/shared/ui/kit/dialog";
import { Label } from "@/shared/ui/kit/label";
import { Switch } from "@/shared/ui/kit/switch";
import { Icons } from "@/shared/ui/icons";
import { useQuery } from "@tanstack/react-query";
import dynamic from "next/dynamic";
import { useState } from "react";
import { useUpdateProjectReviewContext } from "../model/use-update-project-review-context";

const MarkdownEditor = dynamic(
  () => import("@/shared/ui/kit/markdown-editor").then((m) => ({ default: m.MarkdownEditor })),
  { ssr: false, loading: () => <div className="h-64 animate-pulse bg-muted rounded-md" /> },
);

const GUIDELINES_MAX = 50_000;

interface Props {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  projectId: string;
  projectName: string;
}

interface FormState {
  guidelinesMarkdown: string;
  isAutoReviewEnabled: boolean;
  requiresGithubConnection: boolean;
  requiresReviewApp: boolean;
}

function deriveFromData(data: ProjectReviewContextDto | null | undefined): FormState {
  return {
    guidelinesMarkdown: data?.guidelinesMarkdown ?? "",
    isAutoReviewEnabled: data?.isAutoReviewEnabled ?? true,
    requiresGithubConnection: data?.requiresGithubConnection ?? true,
    requiresReviewApp: data?.requiresReviewApp ?? true,
  };
}

/**
 * Author UI для PROJECT-level guidelines AI-проверки. Markdown-редактор +
 * toggle. Загружается lazy на открытии dialog'а через projectReviewContextQueryOptions.
 *
 * Form pattern: `draft ?? deriveFromData(data)` (draft=null означает «use fetched
 * as-is»). Reset draft через `setDraft(null)` после save или на close.
 */
export function ProjectReviewContextDialog({ open, onOpenChange, projectId, projectName }: Props) {
  const { data, isLoading } = useQuery({
    ...projectReviewContextQueryOptions(projectId),
    enabled: open,
  });
  const mutation = useUpdateProjectReviewContext(projectId);
  const [draft, setDraft] = useState<FormState | null>(null);
  const form = draft ?? deriveFromData(data);

  const updateDraft = (next: Partial<FormState>) => {
    const merged = { ...form, ...next };
    if (next.isAutoReviewEnabled === true) {
      merged.requiresGithubConnection = true;
      merged.requiresReviewApp = true;
    }
    if (!merged.requiresGithubConnection || !merged.requiresReviewApp) {
      merged.isAutoReviewEnabled = false;
    }
    setDraft(merged);
  };

  const handleOpenChange = (next: boolean) => {
    if (!next) setDraft(null);
    onOpenChange(next);
  };

  const handleSave = async () => {
    await mutation.mutateAsync({
      guidelinesMarkdown: form.guidelinesMarkdown,
      isAutoReviewEnabled: form.isAutoReviewEnabled,
      requiresGithubConnection: form.requiresGithubConnection,
      requiresReviewApp: form.requiresReviewApp,
    });
    setDraft(null);
    onOpenChange(false);
  };

  return (
    <Dialog open={open} onOpenChange={handleOpenChange}>
      <DialogContent className="sm:max-w-[860px] max-h-[90vh] overflow-y-auto">
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2">
            <Icons.ai size={18} className="text-primary" />
            AI-проверка проекта
          </DialogTitle>
          <DialogDescription>
            {projectName}. Эти инструкции добавляются в prompt AI-проверяльщика для всех заданий
            этого проекта. Опиши общие требования к коду: стиль, обязательные тесты, формат
            коммитов, документация.
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-4 py-2">
          <div className="space-y-2">
            <div className="flex items-center justify-between gap-3">
              <Label className="text-sm font-medium">Guidelines (markdown)</Label>
              <span className="text-xs text-muted-foreground">
                {form.guidelinesMarkdown.length} / {GUIDELINES_MAX}
              </span>
            </div>
            <MarkdownEditor
              value={form.guidelinesMarkdown}
              onChange={(value) => setDraft({ ...form, guidelinesMarkdown: value })}
              placeholder="# Общие правила&#10;&#10;- Все public-API имеют docstrings&#10;- Тесты обязательны для бизнес-логики&#10;- Используем Result<T, Error>, не бросаем exceptions для бизнес-ошибок"
              minHeight={360}
              layout="split"
              defaultViewMode="write"
            />
          </div>

          <div className="flex items-center justify-between gap-3 rounded-lg border border-border/50 p-3">
            <div className="space-y-0.5">
              <Label htmlFor={`project-github-toggle-${projectId}`} className="text-sm">
                Требовать привязку GitHub
              </Label>
              <p className="text-xs text-muted-foreground">
                Включи для PR-задач, где нужна ссылка на репозиторий студента.
              </p>
            </div>
            <Switch
              id={`project-github-toggle-${projectId}`}
              checked={form.requiresGithubConnection}
              onCheckedChange={(value) => updateDraft({ requiresGithubConnection: value })}
              disabled={isLoading || mutation.isPending}
            />
          </div>

          <div className="flex items-center justify-between gap-3 rounded-lg border border-border/50 p-3">
            <div className="space-y-0.5">
              <Label htmlFor={`project-app-toggle-${projectId}`} className="text-sm">
                Требовать GitHub App / review bot
              </Label>
              <p className="text-xs text-muted-foreground">
                Нужен только когда проверка должна читать pull request.
              </p>
            </div>
            <Switch
              id={`project-app-toggle-${projectId}`}
              checked={form.requiresReviewApp}
              onCheckedChange={(value) => updateDraft({ requiresReviewApp: value })}
              disabled={isLoading || mutation.isPending}
            />
          </div>

          <div className="flex items-center justify-between gap-3 rounded-lg border border-border/50 p-3">
            <div className="space-y-0.5">
              <Label htmlFor={`project-rc-toggle-${projectId}`} className="text-sm">
                Включить AI-проверку для проекта
              </Label>
              <p className="text-xs text-muted-foreground">
                Доступно для PR-задач, когда включены GitHub и GitHub App.
              </p>
            </div>
            <Switch
              id={`project-rc-toggle-${projectId}`}
              checked={form.isAutoReviewEnabled}
              onCheckedChange={(value) => updateDraft({ isAutoReviewEnabled: value })}
              disabled={isLoading || mutation.isPending}
            />
          </div>

          {data?.updatedAt && (
            <p className="text-xs text-muted-foreground">
              Обновлено: {new Date(data.updatedAt).toLocaleString("ru-RU")}
            </p>
          )}
        </div>

        <DialogFooter>
          <Button
            type="button"
            variant="outline"
            onClick={() => handleOpenChange(false)}
            disabled={mutation.isPending}
          >
            Отмена
          </Button>
          <Button type="button" onClick={handleSave} disabled={isLoading || mutation.isPending}>
            {mutation.isPending ? "Сохраняем…" : "Сохранить guidelines"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
