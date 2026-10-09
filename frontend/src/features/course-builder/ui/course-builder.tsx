"use client";

import {
  useArchiveCourse,
  usePublishCourse,
  useRestoreCourse,
  type CourseBuilderDto,
} from "@/entities/course";
import { Button } from "@/shared/ui/kit/button";
import { Loader2 } from "lucide-react";
import type { ReactNode } from "react";
import { useQuery } from "@tanstack/react-query";
import { routes } from "@/shared/config/routes";
import { publicProfileQueryOptions } from "@/entities/profile";
import { NotFoundFallback } from "@/shared/ui/components/not-found-fallback";
import { UserAvatar } from "@/shared/ui/components/user-avatar";
import { useCourseBuilder } from "../model/use-course-builder";
import { CourseBuilderMobileTabs } from "./course-builder-mobile-tabs";
import { CourseLandingSettings } from "./course-landing-settings";
import { CourseSettings } from "./course-settings";
import { CourseBuilderCollectionsTab } from "./course-builder-collections-tab";
import { CourseBuilderMaterialsTab } from "./course-builder-materials-tab";
import { ModuleList } from "./module-list";
import { ProjectList } from "./project-list";

interface CourseBuilderProps {
  courseId: string;
  renderStudents?: (args: { courseId: string; course: CourseBuilderDto }) => ReactNode;
  /**
   * Slot для вкладки «Статистика» (#634) — KPI прохождения курса + статистика
   * тестов. Композируется на уровне страницы; feature `course-builder` не зависит
   * от `features/course-statistics`.
   */
  renderStatistics?: (args: { courseId: string; course: CourseBuilderDto }) => ReactNode;
  /**
   * Slot для диалога рассылки объявления подписчикам курса. Композируется
   * на уровне страницы — feature `course-builder` не зависит от
   * `features/notifications-broadcast`. Рендерится только когда курс PUBLISHED.
   */
  renderBroadcastDialog?: (args: { courseId: string; course: CourseBuilderDto }) => ReactNode;
  /**
   * Slot для диалога передачи курса другому автору (#587). Композируется на уровне
   * страницы (gating через `<Can>` там же) — feature `course-builder` не зависит от
   * `features/transfer-course-author`. Показывается для курса любого статуса.
   */
  renderTransferDialog?: (args: { courseId: string; course: CourseBuilderDto }) => ReactNode;
}

export function CourseBuilder({
  courseId,
  renderStudents,
  renderStatistics,
  renderBroadcastDialog,
  renderTransferDialog,
}: CourseBuilderProps) {
  const builder = useCourseBuilder(courseId);
  const { publishCourse, isPending: isPublishing } = usePublishCourse(courseId);
  const { archiveCourse, isPending: isArchiving } = useArchiveCourse(courseId);
  const { restoreCourse, isPending: isRestoring } = useRestoreCourse(courseId);
  // Автор курса (#637) — резолвим имя+аватар по authorId один раз (кеш 5мин), чтобы
  // показать нормального автора со ссылкой на профиль вместо обрезанного GUID. Хук
  // до ранних return'ов (Rules of Hooks); явный `enabled` не плодит inert-кэш `["","..."]`
  // и не шлёт запрос, пока курс не загрузился.
  const { data: author } = useQuery({
    ...publicProfileQueryOptions(builder.course?.authorId ?? ""),
    enabled: !!builder.course?.authorId,
  });

  if (builder.isLoading) {
    return (
      <div className="flex items-center justify-center h-64">
        <Loader2 className="size-6 animate-spin text-muted-foreground" />
      </div>
    );
  }

  if (builder.error || !builder.course) {
    return (
      <NotFoundFallback
        message="Курс не найден"
        backHref={routes.authorCourses}
        backLabel="К списку курсов"
      />
    );
  }

  const course = builder.course;

  return (
    <div className="flex flex-col h-full">
      {/* Slim top bar — publish/archive action (sidebar handles nav + title) */}
      <div className="sticky top-0 z-20 bg-card border-b shadow-sm">
        <div className="flex items-center justify-between gap-2 px-4 py-2.5 sm:px-5">
          <div className="min-w-0">
            <h1 className="truncate text-sm font-semibold text-muted-foreground">{course.title}</h1>
            <div className="mt-0.5 flex items-center gap-1.5 text-[11px] text-muted-foreground/70">
              <UserAvatar
                name={author?.displayName ?? author?.username}
                avatarId={author?.avatarId}
                userId={course.authorId}
                className="size-4"
              />
              <span className="truncate">
                {author?.displayName ?? author?.username ?? shortId(course.authorId)}
              </span>
            </div>
          </div>
          <div className="flex items-center gap-2 shrink-0">
            {renderTransferDialog?.({ courseId, course })}
            {course.status === "PUBLISHED" ? (
              <>
                {renderBroadcastDialog?.({ courseId, course })}
                <Button
                  variant="outline"
                  size="sm"
                  disabled={isArchiving}
                  onClick={() => archiveCourse(courseId)}
                >
                  {isArchiving ? <Loader2 className="size-3.5 animate-spin" /> : null}
                  <span className="hidden sm:inline">Приостановить</span>
                  <span className="sm:hidden">Пауза</span>
                </Button>
              </>
            ) : course.status === "ARCHIVED" ? (
              <Button
                size="sm"
                className="bg-gradient-primary text-primary-foreground border-0 hover:opacity-90"
                disabled={isRestoring}
                onClick={() => restoreCourse(courseId)}
              >
                {isRestoring ? <Loader2 className="size-3.5 animate-spin" /> : null}
                Восстановить
              </Button>
            ) : (
              <Button
                size="sm"
                className="bg-gradient-primary text-primary-foreground border-0 hover:opacity-90"
                disabled={isPublishing}
                onClick={() => publishCourse(courseId)}
              >
                {isPublishing ? <Loader2 className="size-3.5 animate-spin" /> : null}
                Опубликовать
              </Button>
            )}
          </div>
        </div>
      </div>

      {/* Mobile section switcher — desktop uses the sidebar */}
      <CourseBuilderMobileTabs activeTab={builder.activeTab} onTabChange={builder.setActiveTab} />

      {/* Content area */}
      <div className="flex-1">
        <div className="max-w-5xl mx-auto p-4 sm:p-5">
          {builder.activeTab === "modules" && (
            <ModuleList
              courseId={courseId}
              course={builder.course!}
              modules={builder.modules}
              projects={builder.projects}
              onCreateModule={(data) => builder.createModule.createModule(data)}
              isCreatePending={builder.createModule.isPending}
              onUpdateModule={(moduleId, data) =>
                builder.updateModule.updateModule({ moduleId, request: data })
              }
              onDetach={(referenceId) => builder.detachCourseItem.detachCourseItem(referenceId)}
              isDetachPending={builder.detachCourseItem.isPending}
              onMove={builder.handleMoveItem}
            />
          )}

          {builder.activeTab === "projects" && (
            <ProjectList
              courseId={courseId}
              projects={builder.projects}
              onCreateProject={(data) => builder.createProject.createProject(data)}
              isCreatePending={builder.createProject.isPending}
              onUpdateProject={(projectId, data) =>
                builder.updateProject.updateProject({ projectId, request: data })
              }
              onDetach={(referenceId) => builder.detachCourseItem.detachCourseItem(referenceId)}
              isDetachPending={builder.detachCourseItem.isPending}
              onMove={builder.handleMoveItem}
            />
          )}

          {builder.activeTab === "materials" && <CourseBuilderMaterialsTab courseId={courseId} />}

          {builder.activeTab === "collections" && (
            <CourseBuilderCollectionsTab courseId={courseId} />
          )}

          {builder.activeTab === "landing" && (
            <CourseLandingSettings courseId={courseId} course={course} />
          )}

          {builder.activeTab === "settings" && (
            <CourseSettings courseId={courseId} course={course} />
          )}

          {builder.activeTab === "students" && renderStudents?.({ courseId, course })}

          {builder.activeTab === "statistics" && renderStatistics?.({ courseId, course })}
        </div>
      </div>
    </div>
  );
}

function shortId(value: string) {
  return value.length > 8 ? value.slice(0, 8) : value;
}
