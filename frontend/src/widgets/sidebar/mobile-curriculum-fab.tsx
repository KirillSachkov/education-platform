"use client";

import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { courseCurriculumQueryOptions } from "@/entities/course";
import { courseLearningStateQueryOptions } from "@/entities/course-progress";
import { useResolvedCourseAccess } from "@/features/course-learning";
import { Button } from "@/shared/ui/kit/button";
import {
  Drawer,
  DrawerContent,
  DrawerHeader,
  DrawerTitle,
  DrawerTrigger,
} from "@/shared/ui/kit/drawer";
import { Icons } from "@/shared/ui/icons";
import { cn } from "@/shared/lib/css";
import { useCourseId, useCourseSlug } from "@/shared/providers/course-id-provider";
import { useSidebar } from "@/shared/ui/kit/sidebar";
import { CourseSidebarCollections } from "./course-sidebar-collections";
import { CourseSidebarProgram } from "./course-sidebar-program";

/**
 * Кнопка-FAB для открытия программы курса:
 * - на мобиле — всегда видна (sidebar в Sheet);
 * - на десктопе — только когда sidebar автосвёрнут (collapsed=icon), иначе
 *   программа уже видна в обычном sidebar и FAB дублировал бы её.
 */
export function MobileCurriculumFab() {
  const courseId = useCourseId();
  const courseSlug = useCourseSlug();
  const { isMobile, state } = useSidebar();
  const [open, setOpen] = useState(false);

  const { data: curriculum, isLoading } = useQuery(courseCurriculumQueryOptions(courseId));
  const access = useResolvedCourseAccess(courseId, curriculum?.authorId);
  const { data: learningState } = useQuery({
    ...courseLearningStateQueryOptions(courseId),
    enabled: access.isAuthenticated && !!courseId,
  });

  const materialStatuses = new Map(
    (learningState?.materials ?? []).map((m) => [m.materialId, m.status]),
  );
  const issueStatuses = new Map((learningState?.issues ?? []).map((i) => [i.issueId, i.status]));
  const passedQuizIds: ReadonlySet<string> = new Set(learningState?.passedQuizIds ?? []);

  // Last navigated material (lastPosition), not last completed (viewedAt).
  // См. course-sidebar.tsx — после «Отметить изученным» viewedAt-сортировка
  // ошибочно превращала только что завершённый материал в «Продолжить».
  const lastPositionMaterialId =
    learningState?.lastPosition?.entityType === "MATERIAL"
      ? learningState.lastPosition.entityId
      : null;

  if (isLoading || !curriculum) return null;

  const shouldShow = isMobile || state === "collapsed";
  if (!shouldShow) return null;

  // On mobile the bottom-nav (~56px) sits on top of the viewport bottom — bump
  // the FAB above it (incl. safe-area) so it doesn't get hidden. On desktop
  // (collapsed sidebar branch) there's no bottom-nav, so 24px is enough.
  return (
    <div
      className={cn(
        "fixed right-6 z-50",
        isMobile ? "bottom-[calc(env(safe-area-inset-bottom)+5rem)]" : "bottom-6",
      )}
    >
      <Drawer open={open} onOpenChange={setOpen}>
        <DrawerTrigger asChild>
          <Button
            size="icon"
            className="size-14 rounded-full shadow-xl"
            aria-label="Программа курса"
          >
            <Icons.listTree className="size-6" />
          </Button>
        </DrawerTrigger>
        <DrawerContent className="h-[92vh] max-h-[92vh]">
          <DrawerHeader className="px-3 pt-3 pb-2">
            <DrawerTitle className="text-base">Программа курса</DrawerTitle>
          </DrawerHeader>
          <div className="overflow-y-auto px-1 pb-4">
            <CourseSidebarProgram
              sections={curriculum.sections}
              materialStatuses={materialStatuses}
              issueStatuses={issueStatuses}
              passedQuizIds={passedQuizIds}
              hasActiveEnrollment={access.hasActiveEnrollment}
              accessLevel={access.accessLevel}
              lastPositionMaterialId={lastPositionMaterialId}
              onItemClick={() => setOpen(false)}
            />
            <CourseSidebarCollections
              collections={curriculum.collections ?? []}
              courseSlug={courseSlug}
              materialStatuses={materialStatuses}
              hasActiveEnrollment={access.hasActiveEnrollment}
              accessLevel={access.accessLevel}
              onItemClick={() => setOpen(false)}
            />
          </div>
        </DrawerContent>
      </Drawer>
    </div>
  );
}
