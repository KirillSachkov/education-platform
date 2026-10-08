"use client";

import Link from "next/link";
import { Icons } from "@/shared/ui/icons";
import { cn } from "@/shared/lib/css";
import {
  SidebarGroup,
  SidebarGroupContent,
  SidebarGroupLabel,
  SidebarMenu,
  SidebarMenuButton,
  SidebarMenuItem,
} from "@/shared/ui/kit/sidebar";
import type { CollectionDetailDto } from "@/entities/collection";
import type { MaterialProgressStatus } from "@/entities/course-progress";
import type { MaterialFromContext } from "@/shared/config/routes";
import { routes } from "@/shared/config/routes";
import { ItemProgressIndicator } from "@/shared/ui/components/item-progress-indicator";

interface CourseSidebarCollectionProps {
  collection: CollectionDetailDto;
  courseSlug: string;
  /** From-контекст текущего материала — пробрасывается в ссылки, чтобы prev/next (#464) и крошки (#276) продолжали ходить по подборке. */
  from: MaterialFromContext;
  currentMaterialId: string | null;
  materialStatuses: Map<string, string>;
  hasActiveEnrollment: boolean;
  /** Called after an item link is clicked (e.g. close the mobile drawer). */
  onItemClick?: React.MouseEventHandler<HTMLAnchorElement>;
}

/**
 * Дерево подборки в курс-сайдбаре (#496). Когда материал открыт из подборки
 * (`?from=collection:…`), программа курса этот материал не содержит и только
 * путает («не понятно, где оно») — вместо неё показываем состав подборки:
 * секции и материалы, текущий подсвечен, изученные зачёркнуты, недоступные
 * помечены замком (клик по ним ведёт на lock-CTA страницу материала — то же
 * решение, что у prev/next-навигации #464). Программа курса остаётся доступна
 * по ссылке «Программа» в навигации сайдбара выше.
 */
export function CourseSidebarCollection({
  collection,
  courseSlug,
  from,
  currentMaterialId,
  materialStatuses,
  hasActiveEnrollment,
  onItemClick,
}: CourseSidebarCollectionProps) {
  // Повторное вхождение материала в другой секции схлопывается по первому
  // вхождению — зеркалит getCollectionNavigationItems, иначе подсветка
  // активного item'а двоится.
  const seenMaterialIds = new Set<string>();

  return (
    <SidebarGroup className="group-data-[collapsible=icon]:hidden">
      <SidebarGroupLabel className="flex items-center justify-between">
        <span>Подборка</span>
      </SidebarGroupLabel>

      <SidebarGroupContent>
        <Link
          href={routes.courseCollectionDetail(courseSlug, collection.id)}
          onClick={onItemClick}
          className="mx-1 mb-1 flex items-center gap-2 rounded-md px-2 py-2 hover:bg-sidebar-accent transition-colors"
        >
          <Icons.library size={14} className="text-primary shrink-0" />
          <span
            className="flex-1 min-w-0 truncate text-sm font-medium text-sidebar-foreground/85"
            title={collection.title}
          >
            {collection.title}
          </span>
          <Icons.chevronRight size={12} className="text-muted-foreground/70 shrink-0" />
        </Link>

        {collection.sections.map((section) => {
          // Generic-элементы подборок (#491): MATERIAL рендерим как раньше
          // (referenceId = materialId), QUIZ — строкой со ссылкой на /quizzes/{id}.
          const items = section.items.filter((item) => {
            if (seenMaterialIds.has(item.referenceId)) return false;
            seenMaterialIds.add(item.referenceId);
            return true;
          });
          if (items.length === 0) return null;

          return (
            <div key={section.id} className="mb-1">
              {section.title && (
                <p
                  className="px-2 pt-1.5 pb-0.5 text-[11px] font-medium uppercase tracking-wide text-muted-foreground/60 truncate"
                  title={section.title}
                >
                  {section.title}
                </p>
              )}
              <SidebarMenu className="pl-2">
                {items.map((item) => {
                  if (item.itemType === "QUIZ") {
                    const quizTitle = item.quizTitle ?? "Тест";
                    return (
                      <SidebarMenuItem key={`${section.id}:${item.referenceId}`}>
                        <SidebarMenuButton asChild tooltip={quizTitle} className="gap-2 h-auto py-1.5">
                          <Link href={routes.quiz(item.referenceId)} onClick={onItemClick}>
                            {!item.isAccessible && (
                              <Icons.locked
                                size={11}
                                strokeWidth={1.5}
                                className="shrink-0 text-muted-foreground/50"
                              />
                            )}
                            <Icons.quiz size={12} className="shrink-0 text-violet-400/80" />
                            <div
                              className="flex-1 min-w-0 text-[13px] leading-snug truncate"
                              title={quizTitle}
                            >
                              {quizTitle}
                            </div>
                          </Link>
                        </SidebarMenuButton>
                      </SidebarMenuItem>
                    );
                  }

                  const materialTitle = item.material?.title ?? "Материал";
                  const isActive = item.referenceId === currentMaterialId;
                  const rawStatus: MaterialProgressStatus = hasActiveEnrollment
                    ? ((materialStatuses.get(item.referenceId) as MaterialProgressStatus) ??
                      "NOT_VIEWED")
                    : "NOT_VIEWED";
                  const isItemCompleted = rawStatus === "VIEWED";
                  const href = routes.courseMaterial(courseSlug, item.referenceId, { from });

                  return (
                    <SidebarMenuItem key={`${section.id}:${item.referenceId}`}>
                      <SidebarMenuButton
                        asChild
                        isActive={isActive}
                        tooltip={materialTitle}
                        className="gap-2 h-auto py-1.5"
                      >
                        <Link href={href} onClick={onItemClick}>
                          {!item.isAccessible && (
                            <Icons.locked
                              size={11}
                              strokeWidth={1.5}
                              className="shrink-0 text-muted-foreground/50"
                            />
                          )}
                          <div className="w-3.5 h-3.5 flex items-center justify-center shrink-0">
                            <ItemProgressIndicator
                              itemType="material"
                              status={rawStatus}
                              tier="standard"
                              isActive={isActive}
                            />
                          </div>
                          <div
                            className={cn(
                              "flex-1 min-w-0 text-[13px] leading-snug truncate",
                              isItemCompleted &&
                                !isActive &&
                                "line-through text-sidebar-foreground/55",
                              isActive && isItemCompleted && "text-sidebar-foreground font-medium",
                            )}
                            title={materialTitle}
                          >
                            {materialTitle}
                          </div>
                        </Link>
                      </SidebarMenuButton>
                    </SidebarMenuItem>
                  );
                })}
              </SidebarMenu>
            </div>
          );
        })}
      </SidebarGroupContent>
    </SidebarGroup>
  );
}
