"use client";

import { CollectionGrid, courseCollectionsQueryOptions } from "@/entities/collection";
import { routes } from "@/shared/config/routes";
import { Icons } from "@/shared/ui/icons";
import { Skeleton } from "@/shared/ui/kit/skeleton";
import { useQuery } from "@tanstack/react-query";
import Link from "next/link";

interface CourseCollectionsProps {
  courseId: string;
  courseSlug: string;
}

export function CourseCollections({ courseId, courseSlug }: CourseCollectionsProps) {
  const {
    data: collections,
    isLoading,
    isError,
  } = useQuery(courseCollectionsQueryOptions(courseId));

  if (isLoading) {
    return (
      <section className="space-y-2 sm:space-y-3">
        <Skeleton className="h-7 w-40" />
        <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-4">
          <Skeleton className="aspect-[3/4] rounded-2xl" />
          <Skeleton className="aspect-[3/4] rounded-2xl" />
          <Skeleton className="aspect-[3/4] rounded-2xl" />
          <Skeleton className="aspect-[3/4] rounded-2xl" />
        </div>
      </section>
    );
  }

  if (isError) {
    return (
      <section className="space-y-2 sm:space-y-3">
        <h2 className="text-base sm:text-lg md:text-xl font-semibold tracking-tight">Подборки</h2>
        <p className="text-sm text-muted-foreground">Не удалось загрузить данные</p>
      </section>
    );
  }

  if (!collections || collections.length === 0) return null;

  // Pinned-first ranking: автор может закреплять подборки, чтобы они были на виду на курсовой главной.
  // Бэкенд в unpinned-режиме сортирует по updated_at DESC и не учитывает is_pinned, поэтому
  // pinned ранжируем клиентом — закреплённые наверх по pinned_sort_key, остальные — по дате.
  const sorted = [...collections].sort((a, b) => {
    if (a.isPinned !== b.isPinned) return a.isPinned ? -1 : 1;
    if (a.isPinned && b.isPinned) {
      return (a.pinnedSortKey ?? "").localeCompare(b.pinnedSortKey ?? "");
    }
    return b.updatedAt.localeCompare(a.updatedAt);
  });

  return (
    <section className="space-y-2 sm:space-y-3">
      <div className="flex items-end justify-between gap-3">
        <div className="min-w-0">
          <h2 className="text-base sm:text-lg md:text-xl font-semibold tracking-tight">Подборки</h2>
          <p className="text-xs sm:text-sm text-muted-foreground mt-0.5">
            Тематические сборники материалов
          </p>
        </div>
        <Link
          href={routes.courseKnowledgeBase(courseSlug)}
          className="text-xs sm:text-sm text-muted-foreground hover:text-primary transition-colors shrink-0 inline-flex items-center gap-1"
        >
          В базу знаний
          <Icons.arrowRight className="size-3.5" />
        </Link>
      </div>

      <CollectionGrid
        collections={sorted}
        getHref={(collectionId) => routes.courseCollectionDetail(courseSlug, collectionId)}
        title={null}
        gridClassName="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-4"
      />
    </section>
  );
}
