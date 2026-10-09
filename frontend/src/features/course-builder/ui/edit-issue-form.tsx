"use client";

import { toast } from "sonner";
import { type IssueDetailDto } from "@/entities/issue";
import { bindMarkdownAssets, useMarkdownFileUpload, useMarkdownImageUpload } from "@/entities/file";
import dynamic from "next/dynamic";

const MarkdownEditor = dynamic(
  () => import("@/shared/ui/kit/markdown-editor").then((m) => ({ default: m.MarkdownEditor })),
  { ssr: false, loading: () => <div className="h-64 animate-pulse bg-muted rounded-md" /> },
);
import { Button } from "@/shared/ui/kit/button";
import { Input } from "@/shared/ui/kit/input";
import { Label } from "@/shared/ui/kit/label";
import { RadioGroup, RadioGroupItem } from "@/shared/ui/kit/radio-group";
import { Separator } from "@/shared/ui/kit/separator";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/shared/ui/kit/select";
import { SheetDescription, SheetTitle } from "@/shared/ui/kit/sheet";
import { ScrollArea } from "@/shared/ui/kit/scroll-area";
import { Textarea } from "@/shared/ui/kit/textarea";
import { IssueStatusActions } from "./issue-status-actions";
import { zodResolver } from "@hookform/resolvers/zod";
import { ENTITY_ICONS } from "@/shared/config/entity-icons";
import { Controller, useForm, useWatch } from "react-hook-form";
import { issueSchema, type IssueFormData } from "../model/schemas";
import { useUpdateIssue } from "../model/use-update-issue";
import { IssueMaterialsSection } from "./issue-materials-section";
import { IssueExternalLinksSection } from "./issue-external-links-section";
import { ReviewSpecSection } from "./review-spec-section";

interface EditIssueFormProps {
  issue: IssueDetailDto;
  issueId: string;
  projectId: string;
  projectName: string;
  courseId: string;
  onClose: () => void;
  onEditorViewModeChange?: (viewMode: "write" | "split" | "preview") => void;
}

export function EditIssueForm({
  issue,
  issueId,
  projectId,
  projectName,
  courseId,
  onClose,
  onEditorViewModeChange,
}: EditIssueFormProps) {
  const { updateIssue } = useUpdateIssue(issueId, projectId, courseId);

  const issueTarget = { type: "issue", id: issueId } as const;
  const { handleImagePaste } = useMarkdownImageUpload({ targetEntity: issueTarget });
  const { handleFileAttach } = useMarkdownFileUpload({ targetEntity: issueTarget });

  const {
    register,
    handleSubmit,
    control,
    setValue,
    formState: { errors, isSubmitting },
  } = useForm<IssueFormData>({
    resolver: zodResolver(issueSchema),
    defaultValues: {
      title: issue.title,
      content: issue.content ?? "",
      // Issue #358: FREE удалён. Legacy DB-значения коллапсируются миграцией в REGISTERED.
      accessType: issue.accessType === "ENROLLED" ? "ENROLLED" : "REGISTERED",
      submissionMode: issue.submissionMode ?? "PULL_REQUEST",
      selfCheckInstructions: issue.selfCheckInstructions ?? "",
    },
  });
  const watchedTitle = useWatch({ control, name: "title" });
  const watchedAccessType = useWatch({ control, name: "accessType" });
  const watchedSubmissionMode = useWatch({ control, name: "submissionMode" });

  const onSubmit = async (data: IssueFormData) => {
    await updateIssue({
      title: data.title,
      content: data.content ?? "",
      accessType: data.accessType,
      submissionMode: data.submissionMode,
      selfCheckInstructions:
        data.submissionMode === "SELF_CHECK" ? (data.selfCheckInstructions ?? "") : null,
    });

    if (data.content) {
      await bindMarkdownAssets({
        mode: "edit",
        targetEntity: { type: "issue", id: issueId },
        markdownText: data.content,
      }).catch(() => {
        toast.warning("Не удалось привязать изображения к задаче");
      });
    }
  };

  return (
    <form onSubmit={handleSubmit(onSubmit)} className="flex flex-col h-full">
      <div className="border-b border-border/50 px-6 py-4 shrink-0">
        <div className="flex items-center justify-between gap-3 flex-wrap">
          <div className="flex items-center gap-3 min-w-0">
            <div className="size-9 rounded-xl bg-orange/10 flex items-center justify-center shrink-0">
              <ENTITY_ICONS.issue size={16} className="text-orange" />
            </div>
            <div className="min-w-0">
              <div className="flex items-center gap-2">
                <SheetTitle className="text-base leading-tight truncate">
                  {watchedTitle || "Без названия"}
                </SheetTitle>
                {issue.status && (
                  <span className="text-xs px-1.5 py-0.5 rounded border text-muted-foreground shrink-0">
                    {issue.status === "DRAFT"
                      ? "Черновик"
                      : issue.status === "PUBLISHED"
                        ? "Опубликован"
                        : "Архив"}
                  </span>
                )}
              </div>
              <SheetDescription className="text-xs text-muted-foreground truncate mt-0.5">
                {projectName}
              </SheetDescription>
            </div>
          </div>
          <div className="flex items-center gap-2 shrink-0">
            <IssueStatusActions status={issue.status} issueId={issueId} projectId={projectId} />
            <Button type="button" variant="ghost" size="sm" onClick={onClose}>
              Закрыть
            </Button>
          </div>
        </div>
      </div>

      <ScrollArea className="flex-1 min-h-0">
        <div className="shrink-0 px-6 pt-5 pb-4">
          <div className="flex items-end gap-3">
            <div className="flex-1 space-y-1.5">
              <Label htmlFor="edit-issue-title" className="text-sm font-medium">
                Название задачи
              </Label>
              <Input
                id="edit-issue-title"
                {...register("title")}
                placeholder="Введите название задачи"
                className="h-10"
              />
              {errors.title && <p className="text-sm text-destructive">{errors.title.message}</p>}
            </div>
            <Select
              value={watchedAccessType ?? "ENROLLED"}
              onValueChange={(value) =>
                setValue("accessType", value as IssueFormData["accessType"], {
                  shouldDirty: true,
                })
              }
            >
              <SelectTrigger className="h-10 w-40 focus-visible:border-input focus-visible:ring-0">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="PUBLIC">Публичный</SelectItem>
                <SelectItem value="REGISTERED">Для зарегистрированных</SelectItem>
                <SelectItem value="ENROLLED">По записи</SelectItem>
              </SelectContent>
            </Select>
          </div>
        </div>

        <div className="px-6 pb-4 space-y-3">
          <Label className="text-sm font-medium">Способ сдачи</Label>
          <Controller
            name="submissionMode"
            control={control}
            render={({ field }) => (
              <RadioGroup
                value={field.value}
                onValueChange={(value) => field.onChange(value as IssueFormData["submissionMode"])}
                className="grid gap-2 sm:grid-cols-2"
              >
                <Label className="flex cursor-pointer items-start gap-3 rounded-lg border border-border/60 p-3 text-sm">
                  <RadioGroupItem value="PULL_REQUEST" className="mt-0.5" />
                  <span>
                    <span className="block font-medium">Pull request</span>
                    <span className="block text-xs text-muted-foreground mt-0.5">
                      Студент отправляет ссылку на PR.
                    </span>
                  </span>
                </Label>
                <Label className="flex cursor-pointer items-start gap-3 rounded-lg border border-border/60 p-3 text-sm">
                  <RadioGroupItem value="SELF_CHECK" className="mt-0.5" />
                  <span>
                    <span className="block font-medium">Самопроверка</span>
                    <span className="block text-xs text-muted-foreground mt-0.5">
                      Студент подтверждает критерии сам.
                    </span>
                  </span>
                </Label>
              </RadioGroup>
            )}
          />

          {watchedSubmissionMode === "SELF_CHECK" && (
            <div className="space-y-1.5">
              <Label htmlFor="edit-self-check-instructions" className="text-sm">
                Условия самопроверки
              </Label>
              <Textarea
                id="edit-self-check-instructions"
                {...register("selfCheckInstructions")}
                rows={5}
                placeholder="Например: запусти тесты, проверь сценарий вручную, сравни результат с чеклистом."
              />
              {errors.selfCheckInstructions && (
                <p className="text-sm text-destructive">{errors.selfCheckInstructions.message}</p>
              )}
            </div>
          )}
        </div>

        <div className="px-6 pb-4">
          <Label className="text-sm font-medium mb-1.5 block">Описание задачи</Label>
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
                minHeight={400}
                layout="split"
                defaultViewMode="write"
                onViewModeChange={onEditorViewModeChange}
              />
            )}
          />
        </div>

        <Separator />

        <div className="px-6 py-4 pb-4 grid grid-cols-1 md:grid-cols-2 gap-6">
          <IssueMaterialsSection
            issueId={issueId}
            projectId={projectId}
            courseId={courseId}
            materials={issue.internalMaterials}
          />
          <IssueExternalLinksSection
            issueId={issueId}
            projectId={projectId}
            links={issue.externalLinks}
          />
        </div>

        <div className="px-6 pb-6">
          <ReviewSpecSection issueId={issueId} />
        </div>
      </ScrollArea>
      <div className="border-t border-border/50 px-6 py-3 shrink-0">
        <Button
          type="submit"
          size="sm"
          className="bg-orange text-primary-foreground hover:bg-orange/90"
          disabled={isSubmitting}
        >
          {isSubmitting ? "Сохранение..." : "Сохранить"}
        </Button>
      </div>
    </form>
  );
}
