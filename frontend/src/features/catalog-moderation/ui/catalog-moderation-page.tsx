"use client";

import { useInfiniteQuery } from "@tanstack/react-query";
import {
  pendingCatalogListingInfiniteOptions,
  type PendingCatalogCourseDto,
} from "@/entities/course";
import { getCourseKindBadge } from "@/shared/config/course-kind";
import { getErrorMessage } from "@/shared/api";
import { useInfiniteScroll } from "@/shared/hooks/use-infinite-scroll";
import { fileImageSrcOrNull } from "@/shared/lib/file-src";
import { cn } from "@/shared/lib/css";
import { AuthorCredit, ContentImage } from "@/shared/ui/components";
import { Icons } from "@/shared/ui/icons";
import { Badge } from "@/shared/ui/kit/badge";
import { Button } from "@/shared/ui/kit/button";
import { Card } from "@/shared/ui/kit/card";
import { EmptyState } from "@/shared/ui/kit/empty-state";
import { useApproveCourseListing } from "../model/use-approve-course-listing";

/**
 * Очередь модерации витрины (#569, model A — соавтор): PUBLISHED-курсы, ожидающие
 * одобрения к показу в публичном каталоге. Модератор/админ видит, чей это курс
 * (авторский кредит), и одним кликом одобряет показ.
 */
export function CatalogModerationPage() {
  const { data, isLoading, error, hasNextPage, isFetchingNextPage, fetchNextPage } =
    useInfiniteQuery(pendingCatalogListingInfiniteOptions());
  const approve = useApproveCourseListing();

  const setCursorRef = useInfiniteScroll({ hasNextPage, isFetchingNextPage, fetchNextPage });

  const courses = data?.items ?? [];
  const totalCount = data?.totalCount ?? 0;

  return (
    <div className="mx-auto max-w-4xl p-4 sm:p-6">
      <div className="mb-6">
        <h1 className="text-xl font-bold">Модерация витрины</h1>
        <p className="text-sm text-muted-foreground">
          Опубликованные курсы, ожидающие одобрения к показу в каталоге.
          {!isLoading && totalCount > 0 ? ` Ожидают: ${totalCount}.` : ""}
        </p>
      </div>

      {isLoading && (
        <div className="flex justify-center py-12">
          <Icons.loading className="size-6 animate-spin text-muted-foreground" />
        </div>
      )}

      {!isLoading && error && (
        <div className="py-12 text-center text-sm text-destructive">
          {getErrorMessage(error, "Ошибка загрузки очереди модерации")}
        </div>
      )}

      {!isLoading && !error && courses.length === 0 && (
        <EmptyState
          icon={Icons.success}
          title="Очередь пуста"
          description="Все опубликованные курсы одобрены к показу в каталоге."
          variant="dashed"
        />
      )}

      {!isLoading && !error && courses.length > 0 && (
        <div className="flex flex-col gap-3">
          {courses.map((course) => (
            <PendingCourseRow
              key={course.id}
              course={course}
              onApprove={() => approve.mutate({ courseId: course.id, listed: true })}
              isApproving={approve.isPending && approve.variables?.courseId === course.id}
            />
          ))}
        </div>
      )}

      {hasNextPage && (
        <div ref={setCursorRef} className="flex justify-center py-4">
          {isFetchingNextPage && (
            <Icons.loading className="size-5 animate-spin text-muted-foreground" />
          )}
        </div>
      )}
    </div>
  );
}

function PendingCourseRow({
  course,
  onApprove,
  isApproving,
}: {
  course: PendingCatalogCourseDto;
  onApprove: () => void;
  isApproving: boolean;
}) {
  const coverSrc = fileImageSrcOrNull(course.imageId);
  const kindBadge = getCourseKindBadge(course.kind);

  return (
    <Card className="overflow-hidden p-0">
      <div className="flex flex-col gap-3 p-4 sm:flex-row sm:items-center">
        {/* Cover */}
        <div className="relative aspect-video w-full shrink-0 overflow-hidden rounded-lg bg-muted sm:w-40">
          {coverSrc ? (
            <ContentImage
              src={coverSrc}
              alt=""
              fill
              sizes="160px"
              className="object-cover"
            />
          ) : (
            <div className="flex size-full items-center justify-center bg-gradient-to-br from-primary/20 via-primary/10 to-secondary">
              <Icons.course className="size-7 text-muted-foreground/60" />
            </div>
          )}
        </div>

        {/* Meta */}
        <div className="min-w-0 flex-1">
          <div className="mb-1 flex flex-wrap items-center gap-2">
            <h3 className="text-sm font-semibold">{course.title}</h3>
            {kindBadge && (
              <Badge className={cn("border-0 text-[10px]", kindBadge.class)}>
                {kindBadge.label}
              </Badge>
            )}
          </div>
          <AuthorCredit
            name={course.authorDisplayName}
            avatarUrl={course.authorAvatarUrl}
            className="mb-1.5"
          />
          <p className="line-clamp-2 text-xs text-muted-foreground">{course.description}</p>
        </div>

        {/* Action */}
        <div className="shrink-0">
          <Button
            className="w-full min-touch sm:w-auto"
            onClick={onApprove}
            disabled={isApproving}
          >
            {isApproving ? (
              <Icons.loading className="size-4 animate-spin" />
            ) : (
              <Icons.success className="size-4" />
            )}
            Одобрить
          </Button>
        </div>
      </div>
    </Card>
  );
}
