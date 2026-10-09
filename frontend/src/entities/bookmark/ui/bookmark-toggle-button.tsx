"use client";

import { routes } from "@/shared/config/routes";
import { useSession } from "next-auth/react";
import { Bookmark, BookmarkCheck, Loader2, LogIn } from "lucide-react";
import { Button } from "@/shared/ui/kit/button";
import { cn } from "@/shared/lib/css";
import { IconSwap } from "@/shared/ui/components/icon-swap";
import { Tooltip, TooltipContent, TooltipTrigger } from "@/shared/ui/kit/tooltip";
import { useRouter } from "next/navigation";
import type { BookmarkTargetType } from "../types";
import { useBookmarkStatus } from "../model/use-bookmark-status";
import { useBookmarkToggle } from "../model/use-bookmark-toggle";

interface BookmarkToggleButtonProps {
  courseId: string;
  entityType: BookmarkTargetType;
  entityId: string;
  className?: string;
}

export function BookmarkToggleButton({
  courseId,
  entityType,
  entityId,
  className,
}: BookmarkToggleButtonProps) {
  const router = useRouter();
  const session = useSession();
  const isAuthenticated = session.status === "authenticated";

  const { isBookmarked, isLoading } = useBookmarkStatus({
    courseId,
    entityType,
    entityId,
  });
  const { toggleBookmark } = useBookmarkToggle({
    courseId,
    entityType,
    entityId,
  });

  if (!isAuthenticated) {
    return (
      <Tooltip>
        <TooltipTrigger asChild>
          <Button
            variant="outline"
            size="icon"
            aria-label="Войти, чтобы добавить в закладки"
            className={cn("size-9 rounded-xl", className)}
            // Programmatic nav, NOT a <Link>: this button can render inside a
            // card-level <Link> (e.g. material RowCard), and a nested <a> is
            // invalid HTML → hydration error.
            onClick={() => router.push(routes.login)}
          >
            <LogIn size={15} />
          </Button>
        </TooltipTrigger>
        <TooltipContent sideOffset={8}>Войти, чтобы добавить в закладки</TooltipContent>
      </Tooltip>
    );
  }

  if (isLoading) {
    return (
      <Tooltip>
        <TooltipTrigger asChild>
          <Button
            variant="outline"
            size="icon"
            aria-label="Загружаем закладку"
            className={cn("size-9 rounded-xl", className)}
            disabled
          >
            <Loader2 size={15} className="animate-spin" />
          </Button>
        </TooltipTrigger>
        <TooltipContent sideOffset={8}>Загружаем закладку</TooltipContent>
      </Tooltip>
    );
  }

  return (
    <Tooltip>
      <TooltipTrigger asChild>
        <Button
          variant={isBookmarked ? "secondary" : "outline"}
          size="icon"
          aria-label={isBookmarked ? "Убрать из закладок" : "Добавить в закладки"}
          aria-pressed={isBookmarked}
          className={cn(
            "size-9 rounded-xl border-border/70 transition-colors",
            isBookmarked && "bg-primary/10 text-primary hover:bg-primary/15",
            className,
          )}
          // Optimistic: status cache flips in onMutate, so UI reacts instantly.
          // Button stays enabled — the user can rapidly toggle if needed and
          // the last mutation wins.
          onClick={() => void toggleBookmark(isBookmarked)}
        >
          <IconSwap
            activeKey={isBookmarked ? "on" : "off"}
            items={[
              { key: "off", node: <Bookmark size={15} /> },
              { key: "on", node: <BookmarkCheck size={15} /> },
            ]}
          />
        </Button>
      </TooltipTrigger>
      <TooltipContent sideOffset={8}>
        {isBookmarked ? "Убрать из закладок" : "Добавить в закладки"}
      </TooltipContent>
    </Tooltip>
  );
}
