"use client";

import { CollectionCard, myCollectionsQueryOptions } from "@/entities/collection";
import { routes } from "@/shared/config/routes";
import { useCourseSlug } from "@/shared/providers/course-id-provider";
import { Button } from "@/shared/ui/kit/button";
import { ErrorCard } from "@/shared/ui/kit/error-card";
import { useQuery } from "@tanstack/react-query";
import { LayoutGrid, Loader2, Plus } from "lucide-react";
import Link from "next/link";

interface CourseBuilderCollectionsTabProps {
  courseId: string;
}

const STATUS_BADGES: Record<string, { label: string; className: string }> = {
  DRAFT: {
    label: "Черновик",
    className: "border-yellow/30 bg-yellow/10 text-yellow",
  },
  PUBLISHED: {
    label: "Опубликована",
    className: "border-border/50 bg-transparent text-muted-foreground/60",
  },
  ARCHIVED: {
    label: "Архив",
    className: "border-border bg-secondary text-muted-foreground",
  },
};

export function CourseBuilderCollectionsTab({ courseId }: CourseBuilderCollectionsTabProps) {
  const courseSlug = useCourseSlug();
  const { data: collections, isLoading, error } = useQuery(myCollectionsQueryOptions(courseId));

  if (isLoading) {
    return (
      <div className="flex justify-center py-16">
        <Loader2 className="size-6 animate-spin text-muted-foreground" />
      </div>
    );
  }

  if (error) {
    return <ErrorCard error={error} className="py-16" />;
  }

  return (
    <div className="space-y-6">
      <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <div className="min-w-0">
          <h2 className="text-base font-semibold">Подборки курса</h2>
          <p className="text-sm text-muted-foreground mt-0.5">
            Тематические подборки материалов для студентов курса
          </p>
        </div>
        <Button
          size="sm"
          className="bg-gradient-primary text-primary-foreground border-0 hover:opacity-90 shrink-0 self-start sm:self-auto"
          asChild
        >
          <Link
            href={`${routes.authorCollectionCreate}?courseId=${courseId}&courseSlug=${courseSlug}`}
          >
            <Plus size={14} /> Создать подборку
          </Link>
        </Button>
      </div>

      {collections && collections.length === 0 && (
        <div className="border-2 border-dashed rounded-2xl p-10 flex flex-col items-center justify-center text-center">
          <div className="size-12 rounded-xl bg-muted flex items-center justify-center mb-3">
            <LayoutGrid size={22} className="text-muted-foreground" />
          </div>
          <p className="text-sm font-medium mb-1">Нет подборок</p>
          <p className="text-sm text-muted-foreground mb-4">
            Создайте подборку материалов для структурированной подачи контента
          </p>
          <Button variant="outline" size="sm" asChild>
            <Link
              href={`${routes.authorCollectionCreate}?courseId=${courseId}&courseSlug=${courseSlug}`}
            >
              <Plus size={14} /> Создать подборку
            </Link>
          </Button>
        </div>
      )}

      {collections && collections.length > 0 && (
        <div className="space-y-2">
          {collections.map((collection) => (
            <CollectionCard
              key={collection.id}
              collection={collection}
              href={routes.authorCollectionEdit(collection.id, {
                courseId,
                courseSlug,
              })}
              variant="admin-row"
              statusBadge={STATUS_BADGES[collection.status] ?? null}
            />
          ))}
        </div>
      )}
    </div>
  );
}
