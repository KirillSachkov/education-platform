"use client";

import Link from "next/link";
import {
  canAccessItem,
  type CourseAccessLevel,
  type CurriculumCollectionDto,
} from "@/entities/course";
import { routes } from "@/shared/config/routes";
import { cn } from "@/shared/lib/css";
import { Icons } from "@/shared/ui/icons";
import {
  SidebarGroup,
  SidebarGroupContent,
  SidebarGroupLabel,
  SidebarMenu,
  SidebarMenuButton,
  SidebarMenuItem,
} from "@/shared/ui/kit/sidebar";

interface CourseSidebarCollectionsProps {
  collections: CurriculumCollectionDto[];
  courseSlug: string;
  materialStatuses: Map<string, string>;
  hasActiveEnrollment: boolean;
  accessLevel: CourseAccessLevel;
  /** Called after a collection link is clicked (e.g. close the mobile drawer). */
  onItemClick?: React.MouseEventHandler<HTMLAnchorElement>;
}

/**
 * Секция «Подборки» в курс-сайдбаре (#508) — под деревом программы. Показывает
 * PUBLISHED-подборки курса с прогрессом «X/Y»: X — материалы подборки со
 * статусом VIEWED из learning-state, Y — PUBLISHED-материалы подборки из
 * curriculum (согласованы со знаменателями blueprint #496, поэтому общий
 * счётчик курса в шапке сайдбара перестаёт быть «фантомным»). Клик ведёт на
 * detail подборки (partial-access) — замок не блокирует переход, как в КБ.
 */
export function CourseSidebarCollections({
  collections,
  courseSlug,
  materialStatuses,
  hasActiveEnrollment,
  accessLevel,
  onItemClick,
}: CourseSidebarCollectionsProps) {
  if (collections.length === 0) return null;

  return (
    <SidebarGroup className="group-data-[collapsible=icon]:hidden">
      <SidebarGroupLabel>Подборки</SidebarGroupLabel>

      <SidebarGroupContent>
        <SidebarMenu>
          {collections.map((collection) => {
            const completed = collection.materialIds.filter(
              (id) => materialStatuses.get(id) === "VIEWED",
            ).length;
            const isComplete =
              hasActiveEnrollment &&
              collection.itemsCount > 0 &&
              completed >= collection.itemsCount;
            const isLocked = !canAccessItem(collection.accessType, accessLevel);

            return (
              <SidebarMenuItem key={collection.id}>
                <SidebarMenuButton
                  asChild
                  tooltip={collection.title}
                  className="gap-2 h-auto py-1.5"
                >
                  <Link
                    href={routes.courseCollectionDetail(courseSlug, collection.id)}
                    onClick={onItemClick}
                  >
                    {isLocked && (
                      <Icons.locked
                        size={11}
                        strokeWidth={1.5}
                        className="shrink-0 text-muted-foreground/50"
                      />
                    )}
                    <Icons.library
                      size={13}
                      className={cn("shrink-0", isComplete ? "text-green" : "text-primary/80")}
                    />
                    <span
                      className={cn(
                        "flex-1 min-w-0 truncate text-[13px] leading-snug",
                        isComplete && "text-sidebar-foreground/55",
                      )}
                      title={collection.title}
                    >
                      {collection.title}
                    </span>
                    {hasActiveEnrollment && (
                      <span
                        className={cn(
                          "text-xs tabular-nums shrink-0",
                          isComplete ? "text-green" : "text-muted-foreground/60",
                        )}
                      >
                        {completed}/{collection.itemsCount}
                      </span>
                    )}
                  </Link>
                </SidebarMenuButton>
              </SidebarMenuItem>
            );
          })}
        </SidebarMenu>
      </SidebarGroupContent>
    </SidebarGroup>
  );
}
