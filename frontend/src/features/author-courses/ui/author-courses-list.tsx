"use client";

import {
  useArchiveCourse,
  usePublishCourse,
  useRestoreCourse,
  type CourseSummaryDto,
} from "@/entities/course";
import { getErrorMessage } from "@/shared/api";
import { routes } from "@/shared/config/routes";
import { ROLES, useRoles } from "@/shared/auth";
import { formatShortDateWithTime } from "@/shared/lib/date/format";
import { AuthorCredit } from "@/shared/ui/components/author-credit";
import { StatusBadge } from "@/shared/ui/components/status-badge";
import { Icons } from "@/shared/ui/icons";
import { Badge } from "@/shared/ui/kit/badge";
import { Button } from "@/shared/ui/kit/button";
import { Card } from "@/shared/ui/kit/card";
import { DragDropProvider } from "@dnd-kit/react";
import { isSortable, useSortable } from "@dnd-kit/react/sortable";
import {
  Archive,
  ChevronDown,
  ChevronUp,
  FolderKanban,
  GripVertical,
  Loader2,
  Pencil,
  Play,
  Plus,
} from "lucide-react";
import Link from "next/link";
import { useState } from "react";
import type { CourseKind } from "@/shared/config/course-kind";
import { CourseKindFilter } from "@/shared/ui/components/course-kind-filter";
import { useAuthorCourses } from "../model/use-author-courses";
import { useMoveCourse } from "../model/use-move-course";
import { CreateCourseDialog } from "./create-course-dialog";
import { DeleteCourseDialog } from "./delete-course-dialog";
import { EditCourseDialog } from "./edit-course-dialog";

export function AuthorCoursesList() {
  const [createDialogOpen, setCreateDialogOpen] = useState(false);
  const [editCourse, setEditCourse] = useState<CourseSummaryDto | null>(null);
  const [kind, setKind] = useState<CourseKind | "all">("all");
  const { hasAnyRole } = useRoles();
  const canViewPlatformCourses = hasAnyRole([
    ROLES.EDITOR,
    ROLES.MODERATOR,
    ROLES.ADMIN,
    ROLES.OWNER,
  ]);

  const { data, isLoading, error, cursorRef, isFetchingNextPage, hasNextPage } = useAuthorCourses(
    kind === "all" ? undefined : kind,
  );

  const { publishCourse, isPending: isPublishPending } = usePublishCourse();
  const { archiveCourse, isPending: isArchivePending } = useArchiveCourse();
  const { restoreCourse, isPending: isRestorePending } = useRestoreCourse();
  const { moveCourse } = useMoveCourse();

  const courses = data?.items ?? [];
  const title = canViewPlatformCourses ? "Курсы платформы" : "Мои курсы";
  const description = canViewPlatformCourses
    ? "Управляйте курсами всех авторов платформы. В карточках указан владелец курса."
    : "Управляйте своими курсами — перетащите карточку, чтобы изменить порядок";

  const handleDragEnd = (
    event: Parameters<NonNullable<React.ComponentProps<typeof DragDropProvider>["onDragEnd"]>>[0],
  ) => {
    if (event.canceled) return;
    const { source } = event.operation;
    if (!isSortable(source)) return;

    const { initialIndex, index: newIndex } = source;
    if (initialIndex === newIndex) return;

    const movedCourse = courses[initialIndex];
    if (!movedCourse) return;

    // Build the post-move order to compute new neighbours, excluding the dragged item
    // so it doesn't appear as its own neighbour at the target position.
    const remaining = courses.filter((c) => c.id !== movedCourse.id);
    const afterSortKey = newIndex > 0 ? remaining[newIndex - 1]?.sortKey : undefined;
    const beforeSortKey = newIndex < remaining.length ? remaining[newIndex]?.sortKey : undefined;

    void moveCourse({ courseId: movedCourse.id, afterSortKey, beforeSortKey });
  };

  // Touch-friendly reorder fallback (drag is janky on touch). Moves a card
  // one slot up/down using the SAME sort-key neighbour math as drag-end.
  const moveCourseByOffset = (index: number, direction: "up" | "down") => {
    const newIndex = direction === "up" ? index - 1 : index + 1;
    if (newIndex < 0 || newIndex >= courses.length) return;

    const movedCourse = courses[index];
    if (!movedCourse) return;

    const remaining = courses.filter((c) => c.id !== movedCourse.id);
    const afterSortKey = newIndex > 0 ? remaining[newIndex - 1]?.sortKey : undefined;
    const beforeSortKey = newIndex < remaining.length ? remaining[newIndex]?.sortKey : undefined;

    void moveCourse({ courseId: movedCourse.id, afterSortKey, beforeSortKey });
  };

  return (
    <div className="max-w-4xl mx-auto p-6">
      <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between mb-6">
        <div>
          <h1 className="text-xl font-bold">{title}</h1>
          <p className="text-sm text-muted-foreground">{description}</p>
        </div>
        <Button
          className="w-full sm:w-auto bg-gradient-primary text-primary-foreground border-0 hover:opacity-90"
          onClick={() => setCreateDialogOpen(true)}
        >
          <Plus size={15} /> Создать курс
        </Button>
      </div>

      <div className="mb-6 overflow-x-auto">
        <CourseKindFilter value={kind} onChange={setKind} />
      </div>

      {isLoading && (
        <div className="flex justify-center py-12">
          <Loader2 className="h-6 w-6 animate-spin text-muted-foreground" />
        </div>
      )}

      {!isLoading && error && (
        <div className="text-center py-12 text-destructive">
          <p>Не удалось загрузить курсы</p>
          <p className="text-sm mt-1 text-muted-foreground">
            {getErrorMessage(error, "Ошибка загрузки списка курсов")}
          </p>
        </div>
      )}

      {!isLoading && !error && courses.length === 0 && (
        <div className="text-center py-12 text-muted-foreground">
          <p>{canViewPlatformCourses ? "На платформе пока нет курсов" : "У вас пока нет курсов"}</p>
          <p className="text-sm mt-1">Нажмите &laquo;Создать курс&raquo;, чтобы начать</p>
        </div>
      )}

      <DragDropProvider onDragEnd={handleDragEnd}>
        <div className="space-y-4">
          {courses.map((course, idx) => (
            <SortableCourseCard
              key={course.id}
              course={course}
              index={idx}
              canMoveUp={idx > 0}
              canMoveDown={idx < courses.length - 1}
              onMoveUp={() => moveCourseByOffset(idx, "up")}
              onMoveDown={() => moveCourseByOffset(idx, "down")}
              onEdit={() => setEditCourse(course)}
              onArchive={() => archiveCourse(course.id)}
              isArchivePending={isArchivePending}
              onRestore={() => restoreCourse(course.id)}
              isRestorePending={isRestorePending}
              onPublish={() => publishCourse(course.id)}
              isPublishPending={isPublishPending}
              showOwner={canViewPlatformCourses}
            />
          ))}
        </div>
      </DragDropProvider>

      {hasNextPage && (
        <div ref={cursorRef} className="flex justify-center py-4">
          {isFetchingNextPage && <Loader2 className="h-5 w-5 animate-spin text-muted-foreground" />}
        </div>
      )}

      <CreateCourseDialog open={createDialogOpen} onOpenChange={setCreateDialogOpen} />

      {editCourse && (
        <EditCourseDialog
          course={editCourse}
          open={!!editCourse}
          onOpenChange={(open) => !open && setEditCourse(null)}
        />
      )}
    </div>
  );
}

function SortableCourseCard({
  course,
  index,
  canMoveUp,
  canMoveDown,
  onMoveUp,
  onMoveDown,
  onEdit,
  onArchive,
  isArchivePending,
  onRestore,
  isRestorePending,
  onPublish,
  isPublishPending,
  showOwner,
}: {
  course: CourseSummaryDto;
  index: number;
  canMoveUp: boolean;
  canMoveDown: boolean;
  onMoveUp: () => void;
  onMoveDown: () => void;
  onEdit: () => void;
  onArchive: () => void;
  isArchivePending: boolean;
  onRestore: () => void;
  isRestorePending: boolean;
  onPublish: () => void;
  isPublishPending: boolean;
  showOwner: boolean;
}) {
  const { ref, isDragging } = useSortable({ id: course.id, index });

  return (
    <div ref={ref} className={isDragging ? "opacity-50" : undefined}>
      <Card className="p-0 gap-0 overflow-hidden">
        <div className="p-5 flex items-start gap-3">
          <div
            className="cursor-grab touch-none text-muted-foreground/40 hover:text-muted-foreground pt-0.5 hidden md:block"
            aria-label="Перетащить"
            title="Перетащите чтобы изменить порядок"
          >
            <GripVertical className="h-4 w-4" />
          </div>
          {/* Touch reorder fallback — drag is unreliable on touch (md:hidden). */}
          <div className="flex flex-col gap-0.5 md:hidden">
            <Button
              type="button"
              variant="ghost"
              size="icon"
              className="min-touch size-8 text-muted-foreground/60 hover:text-foreground"
              onClick={onMoveUp}
              disabled={!canMoveUp}
              aria-label="Переместить выше"
            >
              <ChevronUp className="h-4 w-4" />
            </Button>
            <Button
              type="button"
              variant="ghost"
              size="icon"
              className="min-touch size-8 text-muted-foreground/60 hover:text-foreground"
              onClick={onMoveDown}
              disabled={!canMoveDown}
              aria-label="Переместить ниже"
            >
              <ChevronDown className="h-4 w-4" />
            </Button>
          </div>
          <div className="min-w-0 flex-1">
            <div className="flex flex-wrap items-center gap-2 mb-2">
              <StatusBadge status={course.status} />
              {/* Курс опубликован, но ещё не одобрен к показу в каталоге (#569). */}
              {course.status === "PUBLISHED" && !course.isCatalogListed && (
                <Badge
                  variant="outline"
                  className="gap-1 border-border/60 bg-muted/40 text-muted-foreground"
                >
                  <Icons.clock className="size-3" />
                  На модерации витрины
                </Badge>
              )}
              {showOwner && (
                <AuthorCredit
                  name={course.authorDisplayName ?? shortId(course.authorId)}
                  avatarUrl={course.authorAvatarUrl}
                />
              )}
            </div>
            <h3 className="text-sm font-semibold mb-1">{course.title}</h3>
            <p className="text-sm text-muted-foreground line-clamp-2 mb-2">{course.description}</p>
            <p className="text-xs text-muted-foreground">
              {formatShortDateWithTime(course.createdAt)}
            </p>
          </div>
        </div>

        <div className="border-t px-5 py-2.5 flex items-center gap-1 flex-wrap">
          <Button variant="ghost" size="sm" className="text-xs" asChild>
            <Link href={routes.authorCourseBuilder(course.slug)} prefetch={false}>
              <FolderKanban size={13} /> Управление
            </Link>
          </Button>
          <Button variant="ghost" size="sm" className="text-xs" onClick={onEdit}>
            <Pencil size={13} /> Редактировать
          </Button>
          {course.status === "PUBLISHED" ? (
            <Button
              variant="ghost"
              size="sm"
              className="text-xs text-muted-foreground hover:bg-secondary sm:ml-auto"
              onClick={onArchive}
              disabled={isArchivePending}
            >
              {isArchivePending ? (
                <Loader2 className="animate-spin" size={13} />
              ) : (
                <Archive size={13} />
              )}{" "}
              <span className="hidden sm:inline">Архивировать</span>
              <span className="sm:hidden">Архив</span>
            </Button>
          ) : course.status === "ARCHIVED" ? (
            <Button
              variant="ghost"
              size="sm"
              className="text-xs text-teal hover:bg-teal/10 sm:ml-auto"
              onClick={onRestore}
              disabled={isRestorePending}
            >
              {isRestorePending ? (
                <Loader2 className="animate-spin" size={13} />
              ) : (
                <Play size={13} />
              )}{" "}
              Восстановить
            </Button>
          ) : (
            <Button
              variant="ghost"
              size="sm"
              className="text-xs text-teal hover:bg-teal/10 sm:ml-auto"
              onClick={onPublish}
              disabled={isPublishPending}
            >
              {isPublishPending ? (
                <Loader2 className="animate-spin" size={13} />
              ) : (
                <Play size={13} />
              )}{" "}
              Опубликовать
            </Button>
          )}
          <DeleteCourseDialog courseId={course.id} />
        </div>
      </Card>
    </div>
  );
}

function shortId(value: string) {
  return value.length > 8 ? value.slice(0, 8) : value;
}
