"use client";

import { CollectionCard, myCollectionsQueryOptions } from "@/entities/collection";
import { routes } from "@/shared/config/routes";
import { Button } from "@/shared/ui/kit/button";
import { EmptyState } from "@/shared/ui/kit/empty-state";
import { useQuery } from "@tanstack/react-query";
import { LayoutGrid, Loader2, Plus } from "lucide-react";
import Link from "next/link";

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

export default function AuthorCollectionsPage() {
  const { data: collections, isLoading } = useQuery(myCollectionsQueryOptions());

  return (
    <div className="mx-auto max-w-5xl p-4 sm:p-6">
      <div className="mb-8 flex flex-col gap-4 md:flex-row md:items-start md:justify-between">
        <div className="space-y-2">
          <h1 className="text-2xl font-bold">Подборки</h1>
          <p className="max-w-2xl text-sm text-muted-foreground">
            Создавайте тематические подборки материалов для студентов.
          </p>
          {!isLoading && collections && (
            <p className="text-sm text-muted-foreground">Всего подборок: {collections.length}</p>
          )}
        </div>

        <Button
          asChild
          className="border-0 bg-gradient-primary text-primary-foreground hover:opacity-90"
        >
          <Link href={routes.authorCollectionCreate}>
            <Plus size={16} />
            Создать подборку
          </Link>
        </Button>
      </div>

      {isLoading && (
        <div className="flex justify-center py-16">
          <Loader2 className="size-6 animate-spin text-muted-foreground" />
        </div>
      )}

      {!isLoading && collections && collections.length === 0 && (
        <EmptyState
          icon={LayoutGrid}
          title="Нет подборок"
          description="Создайте первую подборку, чтобы объединить материалы по темам."
          action={
            <Button asChild>
              <Link href={routes.authorCollectionCreate}>
                <Plus size={16} />
                Создать подборку
              </Link>
            </Button>
          }
          className="rounded-2xl border border-dashed border-border/70 bg-card/70 px-6 py-16"
        />
      )}

      {!isLoading && collections && collections.length > 0 && (
        <div className="space-y-2">
          {collections.map((collection) => (
            <CollectionCard
              key={collection.id}
              collection={collection}
              href={routes.authorCollectionEdit(collection.id, {
                courseId: collection.courseId ?? undefined,
                courseSlug: collection.courseSlug ?? undefined,
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
