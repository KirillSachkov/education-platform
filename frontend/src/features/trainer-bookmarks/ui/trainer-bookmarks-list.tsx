"use client";

import { trainerSessionQueryOptions, type TrainerBookmark } from "@/entities/trainer-session";
import { getErrorMessage } from "@/shared/api";
import { TRAINER_DIFFICULTY_VISUALS } from "@/shared/config/trainer";
import { cn } from "@/shared/lib/css";
import { formatShortDateWithTime } from "@/shared/lib/date/format";
import { DeleteConfirmDialog } from "@/shared/ui/components";
import { Icons } from "@/shared/ui/icons";
import { Button } from "@/shared/ui/kit/button";
import { EmptyState } from "@/shared/ui/kit/empty-state";
import { Skeleton } from "@/shared/ui/kit/skeleton";
import { useInfiniteQuery } from "@tanstack/react-query";
import type { CSSProperties } from "react";
import { useRemoveBookmark } from "../model/use-remove-bookmark";

/**
 * Вкладка «Закладки» хаба тренажёра (#568, тест-разворот): сохранённые вопросы
 * вызывающего с РЕАЛЬНЫМ текстом вопроса (stem/сложность/тема приходят обогащённо)
 * + «Пройти тест» (review-сессия по вопросу) + удаление. Не путать с курсовыми
 * закладками (`features/bookmarks-list`).
 */
interface TrainerBookmarksListProps {
  topicIds?: ReadonlySet<string> | null;
  isScopeLoading?: boolean;
  /**
   * «Пройти тест» по вопросу закладки — хаб стартует review-сессию и навигирует.
   * Необязательно: на странице «Сохранённое» секция = превью (без кнопки теста,
   * есть ссылка «Открыть» в хаб), там launch-оркестрации нет.
   */
  onLaunchTest?: (questionIds: string[]) => void;
  /** Идёт старт сессии — блокируем кнопки. */
  isLaunching?: boolean;
}

export function TrainerBookmarksList({
  topicIds,
  isScopeLoading = false,
  onLaunchTest,
  isLaunching = false,
}: TrainerBookmarksListProps) {
  const bookmarksQuery = useInfiniteQuery(trainerSessionQueryOptions.bookmarksInfiniteOptions());

  if (bookmarksQuery.isPending || isScopeLoading) {
    return (
      <div className="space-y-2">
        {Array.from({ length: 4 }).map((_, index) => (
          <Skeleton key={index} className="h-24 w-full rounded-xl" />
        ))}
      </div>
    );
  }

  if (bookmarksQuery.isError) {
    return (
      <EmptyState
        icon={Icons.error}
        variant="card"
        title="Не удалось загрузить закладки"
        description={getErrorMessage(bookmarksQuery.error, "Попробуйте обновить страницу")}
        action={
          <Button variant="outline" onClick={() => bookmarksQuery.refetch()}>
            <Icons.refresh className="size-4" />
            Попробовать снова
          </Button>
        }
      />
    );
  }

  const bookmarks = bookmarksQuery.data.pages.flatMap((page) => page.items);
  const scopedBookmarks = topicIds
    ? bookmarks.filter((bookmark) => bookmark.topicId !== null && topicIds.has(bookmark.topicId))
    : bookmarks;

  if (bookmarks.length === 0) {
    return (
      <EmptyState
        icon={Icons.bookmark}
        variant="card"
        title="Закладок пока нет"
        description="В разборе ответа жми «В закладки» — отметишь вопрос, чтобы вернуться к нему позже."
      />
    );
  }

  // Под фильтр трека ничего нет и больше страниц нет → закладок этого трека точно нет.
  if (scopedBookmarks.length === 0 && !bookmarksQuery.hasNextPage) {
    return (
      <EmptyState
        icon={Icons.bookmark}
        variant="card"
        title="В этом треке закладок нет"
        description="Сохрани вопрос из выбранного трека — он появится здесь."
      />
    );
  }

  return (
    <div className="space-y-3">
      {scopedBookmarks.length > 0 && (
        <p className="text-sm text-muted-foreground">
          Сохранено вопросов:{" "}
          <span className="font-medium text-foreground">{scopedBookmarks.length}</span>
        </p>
      )}
      <ul className="space-y-2">
        {scopedBookmarks.map((bookmark, index) => (
          <BookmarkRow
            key={bookmark.id}
            bookmark={bookmark}
            index={index}
            isLaunching={isLaunching}
            onStudy={onLaunchTest ? () => onLaunchTest([bookmark.questionId]) : undefined}
          />
        ))}
      </ul>
      {bookmarksQuery.hasNextPage && (
        <div className="flex justify-center pt-1">
          <Button
            variant="outline"
            size="sm"
            onClick={() => bookmarksQuery.fetchNextPage()}
            disabled={bookmarksQuery.isFetchingNextPage}
          >
            {bookmarksQuery.isFetchingNextPage && <Icons.loading className="size-4 animate-spin" />}
            Показать ещё
          </Button>
        </div>
      )}
    </div>
  );
}

function BookmarkRow({
  bookmark,
  index,
  onStudy,
  isLaunching,
}: {
  bookmark: TrainerBookmark;
  index: number;
  onStudy?: () => void;
  isLaunching: boolean;
}) {
  const removeBookmark = useRemoveBookmark();
  const difficultyVisual = bookmark.difficulty
    ? TRAINER_DIFFICULTY_VISUALS[bookmark.difficulty]
    : null;

  return (
    <li
      className="t-enter flex flex-col gap-3 rounded-xl border border-border/60 bg-card p-4"
      style={{ "--t-i": Math.min(index, 12) } as CSSProperties}
    >
      <div className="flex items-start gap-3">
        <span className="flex size-9 shrink-0 items-center justify-center rounded-lg bg-primary/10 text-primary ring-1 ring-inset ring-primary/20">
          <Icons.bookmarkFilled className="size-4" />
        </span>
        <div className="min-w-0 flex-1">
          <p className="line-clamp-3 text-sm leading-snug font-medium">
            {bookmark.stem ?? "Сохранённый вопрос"}
          </p>
          <div className="mt-1.5 flex flex-wrap items-center gap-1.5 text-[11px] text-muted-foreground">
            {bookmark.topicTitle && (
              <span className="font-medium text-foreground/70">{bookmark.topicTitle}</span>
            )}
            {difficultyVisual && (
              <span
                className={cn(
                  "inline-flex items-center rounded-md px-1.5 py-0.5 font-medium",
                  difficultyVisual.badgeClass,
                )}
              >
                {difficultyVisual.label}
              </span>
            )}
            <span className="inline-flex items-center gap-1">
              <Icons.clock className="size-3 shrink-0" />
              {formatShortDateWithTime(bookmark.createdAt)}
            </span>
          </div>
        </div>
      </div>
      <div className="flex items-center gap-2">
        {onStudy && (
          <Button size="sm" className="flex-1 sm:flex-none" onClick={onStudy} disabled={isLaunching}>
            {isLaunching && <Icons.loading className="size-4 animate-spin" />}
            Пройти тест
          </Button>
        )}
        <DeleteConfirmDialog
          title="Удалить закладку?"
          description="Вопрос исчезнет из сохранённых. Сможешь отметить его снова, когда встретишь в тренировке."
          confirmLabel="Удалить"
          isPending={removeBookmark.isPending}
          onConfirm={() => removeBookmark.mutate({ questionId: bookmark.questionId })}
          trigger={
            <Button
              variant="ghost"
              size="icon"
              className="size-9 shrink-0 rounded-lg text-muted-foreground hover:text-destructive"
              aria-label="Удалить закладку"
            >
              <Icons.delete className="size-4" />
            </Button>
          }
        />
      </div>
    </li>
  );
}
