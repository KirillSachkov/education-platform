"use client";

import {
  studentProgressQueryOptions,
  type StudentIssueProgressDto,
} from "@/entities/course-student";
import { type BuilderSectionDto, type CourseBuilderDto } from "@/entities/course";
import { type ModuleItemDto } from "@/entities/module";
import { getErrorMessage } from "@/shared/api";
import { cn } from "@/shared/lib/css";
import type { IssueProgressStatus } from "@/shared/types/status";
import { EmptyState } from "@/shared/ui/kit/empty-state";
import { ScrollArea } from "@/shared/ui/kit/scroll-area";
import { Button } from "@/shared/ui/kit/button";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/shared/ui/kit/select";
import {
  Sheet,
  SheetContent,
  SheetHeader,
  SheetTitle,
} from "@/shared/ui/kit/sheet";
import { Icons } from "@/shared/ui/icons";
import { UserAvatar } from "@/shared/ui/components";
import { useQuery } from "@tanstack/react-query";
import { useSetIssueStatusForUser } from "../model/use-set-issue-status-for-user";
import { useMarkMaterialViewedForUser } from "../model/use-mark-material-viewed-for-user";

/** Полная палитра статусов задания + русские подписи (зеркало StatusBadge). */
const ISSUE_STATUS_OPTIONS: { value: IssueProgressStatus; label: string }[] = [
  { value: "NOT_STARTED", label: "Не начато" },
  { value: "IN_PROGRESS", label: "В работе" },
  { value: "UNDER_REVIEW", label: "На проверке" },
  { value: "REQUESTED_CHANGES", label: "Нужны правки" },
  { value: "COMPLETED", label: "Выполнено" },
];

/**
 * Эти статусы только двигают бейдж и (при уходе из COMPLETED) откатывают XP — НЕ кладут работу в
 * очередь проверки автора (реальный submission не создаётся). Показываем подсказку у опции.
 */
const STATUS_ONLY_BADGE_HINT =
  "Только меняет бейдж и откатывает XP. Не помещает работу в очередь проверки — реальная сдача не создаётся.";

interface StudentProgressPanelProps {
  courseId: string;
  course: CourseBuilderDto;
  userId: string | null;
  studentName: string;
  avatarId: string | null;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

export function StudentProgressPanel({
  courseId,
  course,
  userId,
  studentName,
  avatarId,
  open,
  onOpenChange,
}: StudentProgressPanelProps) {
  const progressQuery = useQuery({
    ...studentProgressQueryOptions(courseId, userId ?? ""),
    enabled: open && !!courseId && !!userId,
  });

  const { markMaterialViewedForUser, pendingMaterialId } =
    useMarkMaterialViewedForUser(courseId, userId ?? "");
  const { setIssueStatusForUser, pendingIssueId } = useSetIssueStatusForUser(
    courseId,
    userId ?? "",
  );

  const progress = progressQuery.data;
  const completedMaterialIds = new Set(
    progress?.completedMaterials.map((m) => m.materialId) ?? [],
  );
  const issuesById = new Map<string, StudentIssueProgressDto>(
    progress?.issues.map((i) => [i.issueId, i]) ?? [],
  );

  const notStarted = !!progress && !progress.enrollmentStarted;

  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent
        side="right"
        size="wide"
        className="max-h-dvh min-h-0 overflow-hidden p-0"
      >
        <SheetHeader className="shrink-0 border-b">
          <SheetTitle className="flex items-center gap-3 pr-8">
            <UserAvatar
              name={studentName}
              avatarId={avatarId}
              userId={userId ?? undefined}
              className="size-9 shrink-0"
            />
            <span className="min-w-0">
              <span className="block truncate text-base">{studentName}</span>
              <span className="block truncate text-xs font-normal text-muted-foreground">
                Прогресс по курсу
              </span>
            </span>
          </SheetTitle>
        </SheetHeader>

        {progressQuery.isLoading ? (
          <div className="flex items-center justify-center py-10 text-muted-foreground">
            <Icons.loading className="size-4 animate-spin mr-2" />
            Загрузка прогресса...
          </div>
        ) : progressQuery.isError ? (
          <div className="space-y-3 px-4 py-8 text-center text-sm text-destructive">
            <p>
              {getErrorMessage(
                progressQuery.error,
                "Не удалось загрузить прогресс студента",
              )}
            </p>
            <Button
              type="button"
              variant="outline"
              onClick={() => progressQuery.refetch()}
            >
              Повторить
            </Button>
          </div>
        ) : (
          <ScrollArea className="min-h-0 flex-1">
            <div className="space-y-5 px-4 pb-6">
              {notStarted && (
                <div className="rounded-lg border border-dashed border-border/60 bg-card/40 px-4 py-3 text-sm text-muted-foreground">
                  Ещё не начинал(а) проходить курс.
                </div>
              )}

              {course.sections.length === 0 ? (
                <EmptyState
                  icon={Icons.listTree}
                  title="В курсе пока нет содержимого"
                  description="Добавьте модули и задания, чтобы отслеживать прогресс."
                />
              ) : (
                course.sections.map((section) => (
                  <SectionBlock
                    key={section.id}
                    section={section}
                    completedMaterialIds={completedMaterialIds}
                    issuesById={issuesById}
                    onMarkMaterial={markMaterialViewedForUser}
                    onSetIssueStatus={setIssueStatusForUser}
                    pendingMaterialId={pendingMaterialId}
                    pendingIssueId={pendingIssueId}
                  />
                ))
              )}
            </div>
          </ScrollArea>
        )}
      </SheetContent>
    </Sheet>
  );
}

type SetIssueStatusFn = (params: {
  issueId: string;
  targetStatus: IssueProgressStatus;
}) => Promise<unknown>;

interface SectionBlockProps {
  section: BuilderSectionDto;
  completedMaterialIds: Set<string>;
  issuesById: Map<string, StudentIssueProgressDto>;
  onMarkMaterial: (materialId: string) => Promise<unknown>;
  onSetIssueStatus: SetIssueStatusFn;
  pendingMaterialId: string | null;
  pendingIssueId: string | null;
}

function SectionBlock({
  section,
  completedMaterialIds,
  issuesById,
  onMarkMaterial,
  onSetIssueStatus,
  pendingMaterialId,
  pendingIssueId,
}: SectionBlockProps) {
  const isProject = section.itemType === "Project";
  const SectionIcon = isProject ? Icons.project : Icons.module;

  return (
    <section className="space-y-2">
      <h3 className="flex items-center gap-2 text-sm font-semibold">
        <SectionIcon size={15} className="text-muted-foreground shrink-0" />
        <span className="truncate">{section.title}</span>
      </h3>

      {section.items.length === 0 ? (
        <p className="pl-1 text-xs text-muted-foreground">Нет элементов</p>
      ) : (
        <ul className="divide-y rounded-lg border">
          {section.items.map((item) => (
            <ItemRow
              key={`${section.id}-${item.id}`}
              item={item}
              isMaterialCompleted={completedMaterialIds.has(item.referenceId)}
              issueProgress={issuesById.get(item.referenceId)}
              onMarkMaterial={onMarkMaterial}
              onSetIssueStatus={onSetIssueStatus}
              pendingMaterialId={pendingMaterialId}
              pendingIssueId={pendingIssueId}
            />
          ))}
        </ul>
      )}
    </section>
  );
}

interface ItemRowProps {
  item: ModuleItemDto;
  isMaterialCompleted: boolean;
  issueProgress: StudentIssueProgressDto | undefined;
  onMarkMaterial: (materialId: string) => Promise<unknown>;
  onSetIssueStatus: SetIssueStatusFn;
  pendingMaterialId: string | null;
  pendingIssueId: string | null;
}

function ItemRow({
  item,
  isMaterialCompleted,
  issueProgress,
  onMarkMaterial,
  onSetIssueStatus,
  pendingMaterialId,
  pendingIssueId,
}: ItemRowProps) {
  const isIssue = item.itemType === "Issue";

  // Staff-override материала (#398): кнопка только для непросмотренных материалов.
  const canMarkMaterial = !isIssue && !isMaterialCompleted;

  const isThisMaterialPending = pendingMaterialId === item.referenceId;
  const isThisIssuePending = pendingIssueId === item.referenceId;

  return (
    <li className="flex items-center justify-between gap-3 px-3 py-2.5">
      <div className="flex min-w-0 items-center gap-2">
        {isIssue ? (
          <Icons.issue size={15} className="text-muted-foreground shrink-0" />
        ) : isMaterialCompleted ? (
          <Icons.completed size={16} className="text-teal shrink-0" />
        ) : (
          <span className="size-4 shrink-0 rounded-full border border-border" />
        )}
        <span
          className={cn(
            "truncate text-sm",
            !isIssue && isMaterialCompleted && "text-muted-foreground",
          )}
        >
          {item.title ?? "Без названия"}
        </span>
      </div>

      <div className="flex shrink-0 items-center gap-2">
        {isIssue ? (
          <IssueStatusSelect
            status={
              (issueProgress?.status as IssueProgressStatus | undefined) ??
              "NOT_STARTED"
            }
            isPending={isThisIssuePending}
            disabled={pendingIssueId !== null}
            onChange={(targetStatus) =>
              void onSetIssueStatus({ issueId: item.referenceId, targetStatus })
            }
          />
        ) : (
          <>
            {canMarkMaterial && (
              <Button
                type="button"
                size="sm"
                variant="outline"
                className="h-7 px-2 text-xs text-teal border-teal/30 hover:bg-teal/10"
                disabled={pendingMaterialId !== null}
                onClick={() => void onMarkMaterial(item.referenceId)}
                title="Отметить материал изученным за студента"
              >
                {isThisMaterialPending ? (
                  <Icons.loading size={13} className="animate-spin" />
                ) : (
                  <Icons.completed size={13} />
                )}
                Отметить изученным
              </Button>
            )}
            {isMaterialCompleted ? (
              <span className="text-xs font-medium text-teal">Изучено</span>
            ) : (
              <span className="text-xs text-muted-foreground">—</span>
            )}
          </>
        )}
      </div>
    </li>
  );
}

interface IssueStatusSelectProps {
  status: IssueProgressStatus;
  isPending: boolean;
  disabled: boolean;
  onChange: (targetStatus: IssueProgressStatus) => void;
}

/**
 * Staff-override (#518): селект полной палитры статусов задания. Переключение сразу шлёт мутацию.
 * Для UNDER_REVIEW / REQUESTED_CHANGES — подсказка, что это только бейдж + откат XP, без очереди
 * проверки (реальной сдачи нет).
 */
function IssueStatusSelect({
  status,
  isPending,
  disabled,
  onChange,
}: IssueStatusSelectProps) {
  return (
    <Select
      value={status}
      disabled={disabled}
      onValueChange={(value) => {
        const next = value as IssueProgressStatus;
        if (next !== status) {
          onChange(next);
        }
      }}
    >
      <SelectTrigger
        size="sm"
        className="h-7 w-[150px] text-xs"
        aria-label="Статус задания студента"
      >
        {isPending ? (
          <Icons.loading size={13} className="animate-spin" />
        ) : (
          <SelectValue />
        )}
      </SelectTrigger>
      <SelectContent align="end">
        {ISSUE_STATUS_OPTIONS.map((option) => {
          const isBadgeOnly =
            option.value === "UNDER_REVIEW" ||
            option.value === "REQUESTED_CHANGES";
          return (
            <SelectItem
              key={option.value}
              value={option.value}
              title={isBadgeOnly ? STATUS_ONLY_BADGE_HINT : undefined}
            >
              <span className="flex flex-col">
                <span>{option.label}</span>
                {isBadgeOnly && (
                  <span className="text-[10px] leading-tight text-muted-foreground">
                    только бейдж, без очереди проверки
                  </span>
                )}
              </span>
            </SelectItem>
          );
        })}
      </SelectContent>
    </Select>
  );
}
