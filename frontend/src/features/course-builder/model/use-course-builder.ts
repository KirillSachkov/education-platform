"use client";

import type { BuilderSectionDto } from "@/entities/course";
import { courseBuilderQueryOptions } from "@/entities/course";
import { compareSortKey } from "@/shared/lib/sort-key";
import { useQuery } from "@tanstack/react-query";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { useCreateModule } from "./use-create-module";
import { useCreateProject } from "./use-create-project";
import { useDetachCourseItem } from "./use-detach-course-item";
import { useMoveCourseItem } from "./use-move-course-item";
import { useUpdateModule } from "./use-update-module";
import { useUpdateProject } from "./use-update-project";

export type ActiveTab =
  | "modules"
  | "projects"
  | "materials"
  | "collections"
  | "landing"
  | "students"
  | "statistics"
  | "settings"
  | "preview";

const VALID_TABS: readonly ActiveTab[] = [
  "modules",
  "projects",
  "materials",
  "collections",
  "landing",
  "students",
  "statistics",
  "settings",
  "preview",
];

export function useCourseBuilder(courseId: string) {
  const {
    data: builderData,
    isLoading,
    error,
  } = useQuery(courseBuilderQueryOptions(courseId));

  const pathname = usePathname();
  const router = useRouter();
  const searchParams = useSearchParams();
  const tabParam = searchParams.get("tab");
  const activeTab: ActiveTab = VALID_TABS.includes(tabParam as ActiveTab)
    ? (tabParam as ActiveTab)
    : "modules";

  const setActiveTab = (tab: ActiveTab) => {
    const params = new URLSearchParams(searchParams.toString());
    params.set("tab", tab);
    router.replace(`${pathname}?${params.toString()}`, { scroll: false });
  };

  const createModule = useCreateModule(courseId);
  const createProject = useCreateProject(courseId);
  const updateModule = useUpdateModule(courseId);
  const updateProject = useUpdateProject(courseId);
  const detachCourseItem = useDetachCourseItem(courseId);
  const moveCourseItem = useMoveCourseItem(courseId);

  const modules = (builderData?.sections ?? [])
    .filter((s) => s.itemType === "Module")
    .sort((a, b) => compareSortKey(a.sortKey, b.sortKey));

  const projects = (builderData?.sections ?? [])
    .filter((s) => s.itemType === "Project")
    .sort((a, b) => compareSortKey(a.sortKey, b.sortKey));

  const handleMoveItem = async (
    items: BuilderSectionDto[],
    referenceId: string,
    initialIndex: number,
    newIndex: number,
  ) => {
    const reordered = [...items];
    const [removed] = reordered.splice(initialIndex, 1);
    reordered.splice(newIndex, 0, removed);

    const afterSortKey =
      newIndex > 0 ? reordered[newIndex - 1].sortKey : undefined;
    const beforeSortKey =
      newIndex < reordered.length - 1
        ? reordered[newIndex + 1].sortKey
        : undefined;

    await moveCourseItem.moveCourseItem({
      referenceId,
      request: { afterSortKey, beforeSortKey },
    });
  };

  return {
    course: builderData,
    isLoading,
    error,
    activeTab,
    setActiveTab,
    modules,
    projects,
    // Mutations
    createModule,
    createProject,
    updateModule,
    updateProject,
    detachCourseItem,
    moveCourseItem,
    handleMoveItem,
  };
}
