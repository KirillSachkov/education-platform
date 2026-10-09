"use client";

import type { BuilderSectionDto } from "@/entities/course";
import { bindMarkdownAssets, useMarkdownFileUpload, useMarkdownImageUpload } from "@/entities/file";
import { issueDetailQueryOptions } from "@/entities/issue";
import { projectsQueryOptions } from "@/entities/project";
import { Button } from "@/shared/ui/kit/button";
import { Input } from "@/shared/ui/kit/input";
import { Label } from "@/shared/ui/kit/label";
import { RadioGroup, RadioGroupItem } from "@/shared/ui/kit/radio-group";
import { Textarea } from "@/shared/ui/kit/textarea";
import dynamic from "next/dynamic";

const MarkdownEditor = dynamic(
  () => import("@/shared/ui/kit/markdown-editor").then((m) => ({ default: m.MarkdownEditor })),
  { ssr: false, loading: () => <div className="h-64 animate-pulse bg-muted rounded-md" /> },
);
import { Sheet, SheetContent, SheetDescription, SheetTitle } from "@/shared/ui/kit/sheet";
import { DragDropProvider } from "@dnd-kit/react";
import { isSortable } from "@dnd-kit/react/sortable";
import { zodResolver } from "@hookform/resolvers/zod";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { FolderKanban, Loader2, Plus } from "lucide-react";
import { ENTITY_ICONS } from "@/shared/config/entity-icons";
import { useState } from "react";
import { Controller, useForm, useWatch } from "react-hook-form";
import { useCreateIssue } from "../model/use-create-issue";
import { issueSchema, issueDefaultValues, type IssueFormData } from "../model/schemas";
import { CreateProjectDialog } from "./create-project-dialog";
import { EditIssueForm } from "./edit-issue-form";
import { EditProjectDialog } from "./edit-project-dialog";
import { ProjectCard } from "./project-card";

interface ProjectListProps {
  courseId: string;
  projects: BuilderSectionDto[];
  onCreateProject: (data: {
    title: string;
    description: string;
    detailedDescription: string;
    requiresGithubConnection?: boolean;
    requiresReviewApp?: boolean;
    isAutoReviewEnabled?: boolean;
  }) => Promise<unknown>;
  isCreatePending: boolean;
  onUpdateProject: (
    projectId: string,
    data: { title: string; description: string; detailedDescription: string },
  ) => Promise<unknown>;
  onDetach: (referenceId: string) => Promise<unknown>;
  isDetachPending: boolean;
  onMove: (
    items: BuilderSectionDto[],
    referenceId: string,
    oldIndex: number,
    newIndex: number,
  ) => Promise<void>;
}

export function ProjectList({
  courseId,
  projects,
  onCreateProject,
  isCreatePending,
  onUpdateProject,
  onDetach,
  isDetachPending,
  onMove,
}: ProjectListProps) {
  const [createProjectOpen, setCreateProjectOpen] = useState(false);
  const [editProjectData, setEditProjectData] = useState<{
    referenceId: string;
    title: string;
    description: string;
    detailedDescription: string;
  } | null>(null);

  // Unified issue sheet state: null=closed, issueId=null means create mode, issueId=string means edit mode
  const [issueSheet, setIssueSheet] = useState<{
    projectId: string;
    projectName: string;
    issueId: string | null;
  } | null>(null);

  return (
    <div>
      <div className="flex items-center justify-between gap-3 mb-5">
        <div className="min-w-0">
          <h2 className="text-base font-semibold">Проекты</h2>
          <p className="text-sm text-muted-foreground mt-0.5">Практические задания для студентов</p>
        </div>
        <Button
          size="sm"
          className="bg-orange text-primary-foreground border-0 hover:opacity-90 shrink-0"
          onClick={() => setCreateProjectOpen(true)}
          disabled={isCreatePending}
        >
          <Plus size={14} /> <span className="hidden sm:inline">Проект</span>
        </Button>
      </div>

      {projects.length === 0 && (
        <div className="border-2 border-dashed rounded-2xl p-10 flex flex-col items-center justify-center text-center">
          <div className="size-12 rounded-xl bg-muted flex items-center justify-center mb-3">
            <FolderKanban size={22} className="text-muted-foreground" />
          </div>
          <p className="text-sm font-medium mb-1">Нет проектов</p>
          <p className="text-sm text-muted-foreground mb-4">
            Создайте проект с практическими задачами
          </p>
          <Button
            variant="outline"
            size="sm"
            onClick={() => setCreateProjectOpen(true)}
            disabled={isCreatePending}
          >
            <Plus size={14} /> Создать проект
          </Button>
        </div>
      )}

      <DragDropProvider
        onDragEnd={(event) => {
          if (event.canceled) return;
          const { source } = event.operation;
          if (!isSortable(source)) return;
          const { initialIndex, index } = source;
          if (initialIndex === index) return;
          onMove(projects, source.id as string, initialIndex, index);
        }}
      >
        <div className="space-y-4">
          {projects.map((project, pIdx) => (
            <ProjectCard
              key={project.id}
              project={project}
              index={pIdx}
              courseId={courseId}
              onEdit={() =>
                setEditProjectData({
                  referenceId: project.id,
                  title: project.title ?? "",
                  description: project.description ?? "",
                  detailedDescription: project.detailedDescription ?? "",
                })
              }
              onDetach={() => onDetach(project.id)}
              isDetachPending={isDetachPending}
              onCreateIssue={() =>
                setIssueSheet({
                  projectId: project.id,
                  projectName: project.title ?? "",
                  issueId: null,
                })
              }
              isFirst={pIdx === 0}
              isLast={pIdx === projects.length - 1}
              onMoveUp={() => onMove(projects, project.id, pIdx, pIdx - 1)}
              onMoveDown={() => onMove(projects, project.id, pIdx, pIdx + 1)}
            />
          ))}
        </div>
      </DragDropProvider>

      {projects.length > 0 && (
        <Button
          variant="outline"
          onClick={() => setCreateProjectOpen(true)}
          className="w-full mt-3"
          disabled={isCreatePending}
        >
          <Plus size={14} /> Добавить проект
        </Button>
      )}

      <CreateProjectDialog
        open={createProjectOpen}
        onOpenChange={setCreateProjectOpen}
        onSubmit={(data) => {
          // Цепочка работает в фоне; форма уже закрыта диалогом.
          void (async () => {
            const result = (await onCreateProject({
              title: data.title,
              description: data.description,
              detailedDescription: data.detailedDescription,
              requiresGithubConnection: data.requiresGithubConnection,
              requiresReviewApp: data.requiresReviewApp,
              isAutoReviewEnabled: data.isAutoReviewEnabled,
            }).catch(() => undefined)) as { result?: string } | undefined;
            const projectId = result?.result;
            if (!projectId) return;

            if (data.detailedDescription) {
              await bindMarkdownAssets({
                mode: "create",
                draftId: data.draftId,
                targetEntity: { type: "project", id: projectId },
                markdownText: data.detailedDescription,
              }).catch((err) => {
                console.error("Failed to bind project markdown assets", err);
              });
            }
          })();
        }}
      />

      {editProjectData && (
        <EditProjectDialog
          open={!!editProjectData}
          projectId={editProjectData.referenceId}
          onOpenChange={(open) => {
            if (!open) setEditProjectData(null);
          }}
          initialData={{
            title: editProjectData.title,
            description: editProjectData.description,
            detailedDescription: editProjectData.detailedDescription,
          }}
          onSubmit={(data) => {
            const projectId = editProjectData.referenceId;
            void (async () => {
              await onUpdateProject(projectId, data).catch(() => undefined);
              await bindMarkdownAssets({
                mode: "edit",
                targetEntity: { type: "project", id: projectId },
                markdownText: data.detailedDescription,
              }).catch((err) => {
                console.error("Failed to sync project markdown assets", err);
              });
            })();
            setEditProjectData(null);
          }}
        />
      )}

      {issueSheet && (
        <IssueSheet
          state={issueSheet}
          courseId={courseId}
          onOpenChange={(open) => {
            if (!open) setIssueSheet(null);
          }}
        />
      )}
    </div>
  );
}

// ---------- Unified Issue Sheet ----------

function IssueSheet({
  state,
  courseId,
  onOpenChange,
}: {
  state: { projectId: string; projectName: string; issueId: string | null };
  courseId: string;
  onOpenChange: (open: boolean) => void;
}) {
  return (
    <Sheet open onOpenChange={onOpenChange}>
      <SheetContent
        side="right"
        size="wide"
        showCloseButton={false}
        className="p-0 gap-0 sm:max-w-[56vw]"
      >
        {state.issueId ? (
          <IssueSheetEditContent
            issueId={state.issueId}
            projectId={state.projectId}
            projectName={state.projectName}
            courseId={courseId}
            onClose={() => onOpenChange(false)}
          />
        ) : (
          <IssueSheetCreateContent
            projectId={state.projectId}
            projectName={state.projectName}
            onClose={() => onOpenChange(false)}
          />
        )}
      </SheetContent>
    </Sheet>
  );
}

function IssueSheetEditContent({
  issueId,
  projectId,
  projectName,
  courseId,
  onClose,
}: {
  issueId: string;
  projectId: string;
  projectName: string;
  courseId: string;
  onClose: () => void;
}) {
  const { data: issue, isLoading } = useQuery({
    ...issueDetailQueryOptions(issueId),
    enabled: !!issueId,
  });

  if (isLoading || !issue) {
    return (
      <>
        <SheetTitle className="sr-only">Загрузка задачи</SheetTitle>
        <div className="flex items-center justify-center h-full">
          <Loader2 className="size-6 animate-spin text-muted-foreground" />
        </div>
      </>
    );
  }

  return (
    <EditIssueForm
      key={issue.updatedAt}
      issue={issue}
      issueId={issueId}
      projectId={projectId}
      projectName={projectName}
      courseId={courseId}
      onClose={onClose}
    />
  );
}

function IssueSheetCreateContent({
  projectId,
  projectName,
  onClose,
}: {
  projectId: string;
  projectName: string;
  onClose: () => void;
}) {
  const queryClient = useQueryClient();
  const { createIssue } = useCreateIssue(projectId);

  // Stable draftId for the lifetime of this component instance.
  // useState with initializer guarantees a single value per mount;
  // useMemo can be discarded by React Compiler and is not safe for identity.
  const [draftId] = useState(() => crypto.randomUUID());
  const { handleImagePaste } = useMarkdownImageUpload({ draftId });
  const { handleFileAttach } = useMarkdownFileUpload({ draftId });

  const {
    register,
    handleSubmit,
    control,
    formState: { errors },
  } = useForm<IssueFormData>({
    resolver: zodResolver(issueSchema),
    defaultValues: issueDefaultValues,
  });
  const watchedSubmissionMode = useWatch({ control, name: "submissionMode" });

  // Закрываем sheet сразу. Цепочка create → bind assets → invalidate
  // продолжает работать в фоне; toast'ы из useCreateIssue показывают результат.
  // Транзишен в edit-mode заменён на закрытие — юзер увидит карточку в списке
  // и зайдёт в неё кликом, если нужно дописать.
  const onSubmit = (data: IssueFormData) => {
    onClose();
    void (async () => {
      const result = await createIssue({
        title: data.title,
        content: data.content ?? "",
        submissionMode: data.submissionMode,
        selfCheckInstructions:
          data.submissionMode === "SELF_CHECK" ? (data.selfCheckInstructions ?? "") : null,
      }).catch(() => undefined);

      if (!result?.result) return;
      const issueId = result.result;

      if (data.content && draftId) {
        await bindMarkdownAssets({
          mode: "create",
          draftId,
          targetEntity: { type: "issue", id: issueId },
          markdownText: data.content,
        }).catch((err) => {
          console.error("Failed to bind markdown assets", err);
        });
      }

      await queryClient.invalidateQueries({
        queryKey: [projectsQueryOptions.baseKey, projectId, "detail"],
      });
    })();
  };

  return (
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
            <Button type="button" variant="ghost" size="sm" onClick={onClose}>
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

      <div className="shrink-0 px-6 pt-4 space-y-3">
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
            <Label htmlFor="self-check-instructions" className="text-sm">
              Условия самопроверки
            </Label>
            <Textarea
              id="self-check-instructions"
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
            />
          )}
        />
      </div>
    </form>
  );
}
