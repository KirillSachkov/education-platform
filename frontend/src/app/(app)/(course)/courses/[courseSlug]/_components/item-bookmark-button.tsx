"use client";

import { useBookmarkStatus, useBookmarkToggle } from "@/entities/bookmark";
import type { BookmarkTargetType } from "@/entities/bookmark";
import { Icons } from "@/shared/ui/icons";
import { cn } from "@/shared/lib/css";
import { Tooltip, TooltipContent, TooltipTrigger } from "@/shared/ui/kit/tooltip";

interface ItemBookmarkButtonProps {
  courseId: string;
  entityType: BookmarkTargetType;
  entityId: string;
}

/**
 * Компактная кнопка-закладка для строк программы/заданий.
 * Использует BookmarkStatusProvider на странице — статусы тянутся одним запросом.
 */
export function ItemBookmarkButton({ courseId, entityType, entityId }: ItemBookmarkButtonProps) {
  const { isBookmarked, isAuthenticated, isLoading } = useBookmarkStatus({
    courseId,
    entityType,
    entityId,
  });
  const { toggleBookmark } = useBookmarkToggle({
    courseId,
    entityType,
    entityId,
  });

  if (!isAuthenticated) return null;

  return (
    <Tooltip>
      <TooltipTrigger asChild>
        <button
          type="button"
          disabled={isLoading}
          onClick={(e) => {
            e.preventDefault();
            e.stopPropagation();
            void toggleBookmark(isBookmarked);
          }}
          className={cn(
            "size-8 rounded-md flex items-center justify-center transition-colors",
            "text-muted-foreground/40 hover:text-foreground hover:bg-muted/50",
            isBookmarked && "text-primary hover:text-primary",
          )}
          aria-label={isBookmarked ? "Убрать из закладок" : "Добавить в закладки"}
        >
          {isBookmarked ? (
            <Icons.bookmarkFilled size={16} strokeWidth={2} />
          ) : (
            <Icons.bookmark size={16} strokeWidth={1.75} />
          )}
        </button>
      </TooltipTrigger>
      <TooltipContent sideOffset={6}>
        {isBookmarked ? "Убрать из закладок" : "Добавить в закладки"}
      </TooltipContent>
    </Tooltip>
  );
}
