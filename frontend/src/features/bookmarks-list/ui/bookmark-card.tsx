"use client";

import { CalendarDays, ChevronRight, Lock, Trash2 } from "lucide-react";
import { resolveLockCopy } from "@/shared/lib/lock-copy";
import { ENTITY_ICONS } from "@/shared/config/entity-icons";
import { useRouter } from "next/navigation";
import {
  useBookmarkToggle,
  type BookmarkedMaterialDto,
  type BookmarkTargetType,
} from "@/entities/bookmark";
import { routes } from "@/shared/config/routes";
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
  AlertDialogTrigger,
} from "@/shared/ui/kit/alert-dialog";
import { Button } from "@/shared/ui/kit/button";
import { Card } from "@/shared/ui/kit/card";
import { cn } from "@/shared/lib/css";
import { formatFullDateWithTime } from "@/shared/lib/date";

interface BookmarkCardProps {
  item: BookmarkedMaterialDto;
}

function isMaterialTarget(type: BookmarkTargetType) {
  return type === "Material" || type === "Lesson";
}

function getTargetHref(item: BookmarkedMaterialDto) {
  return isMaterialTarget(item.target.type)
    ? routes.courseMaterial(item.courseSlug, item.target.id)
    : routes.courseIssue(item.courseSlug, item.target.id);
}

function getTargetLabel(entityType: BookmarkTargetType) {
  return isMaterialTarget(entityType) ? "Материал" : "Задача";
}

export function BookmarkCard({ item }: BookmarkCardProps) {
  const router = useRouter();
  const { toggleBookmark, isPending } = useBookmarkToggle({
    courseId: item.courseId,
    entityType: item.target.type,
    entityId: item.target.id,
  });
  const href = getTargetHref(item);

  return (
    <Card
      className="p-0 gap-0 transition-colors hover:border-primary/25 hover:bg-accent/20 cursor-pointer"
      onClick={() => router.push(href)}
      role="link"
      tabIndex={0}
      onKeyDown={(event) => {
        if (event.target === event.currentTarget && (event.key === "Enter" || event.key === " ")) {
          event.preventDefault();
          router.push(href);
        }
      }}
    >
      <div className="flex items-center gap-3 p-4">
        <div
          className={cn(
            "size-11 rounded-2xl border flex items-center justify-center shrink-0",
            isMaterialTarget(item.target.type)
              ? "border-primary/20 bg-primary/10 text-primary"
              : "border-orange/20 bg-orange/10 text-orange",
          )}
        >
          {isMaterialTarget(item.target.type) ? (
            <ENTITY_ICONS.lesson size={18} />
          ) : (
            <ENTITY_ICONS.issue size={18} />
          )}
        </div>

        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-1.5 text-xs text-muted-foreground min-w-0 overflow-hidden">
            <span className="truncate">{item.courseTitle}</span>
            <span className="shrink-0">/</span>
            <span className="truncate">{item.sectionTitle ?? item.sectionType}</span>
            <span className="shrink-0">/</span>
            <span className="shrink-0">{getTargetLabel(item.target.type)}</span>
          </div>

          <div className="mt-1 flex items-center gap-2 min-w-0">
            <span
              className={cn(
                "min-w-0 flex-1 truncate text-sm font-semibold leading-tight transition-colors group-hover:text-primary",
                !item.isAccessible && "text-muted-foreground",
              )}
            >
              {item.title}
            </span>
            {!item.isAccessible && item.lockReason && (
              <span
                className="inline-flex items-center gap-1 shrink-0 text-[10px] font-semibold text-muted-foreground"
                title={resolveLockCopy(item.lockReason, item.courseTitle).shortHint}
              >
                <Lock size={11} />
                Закрыто
              </span>
            )}
          </div>

          <div className="mt-1.5 flex items-center gap-1.5 text-xs text-muted-foreground">
            <CalendarDays size={12} className="shrink-0" />
            <span>{formatFullDateWithTime(item.createdAt)}</span>
          </div>
        </div>

        <div className="flex items-center gap-1 shrink-0">
          <AlertDialog>
            <AlertDialogTrigger asChild>
              <Button
                variant="ghost"
                size="icon"
                className="size-11 rounded-lg text-muted-foreground hover:text-destructive"
                aria-label="Удалить закладку"
                disabled={isPending}
                onClick={(event) => event.stopPropagation()}
              >
                <Trash2 size={14} />
              </Button>
            </AlertDialogTrigger>
            <AlertDialogContent
              onClick={(event) => event.stopPropagation()}
              onKeyDown={(event) => event.stopPropagation()}
            >
              <AlertDialogHeader>
                <AlertDialogTitle>Удалить закладку?</AlertDialogTitle>
                <AlertDialogDescription>
                  {`"${item.title}" исчезнет из списка закладок. Вы сможете добавить материал снова в любой момент.`}
                </AlertDialogDescription>
              </AlertDialogHeader>
              <AlertDialogFooter>
                <AlertDialogCancel disabled={isPending}>Отмена</AlertDialogCancel>
                <AlertDialogAction onClick={() => toggleBookmark(true)} disabled={isPending}>
                  Удалить
                </AlertDialogAction>
              </AlertDialogFooter>
            </AlertDialogContent>
          </AlertDialog>

          <Button
            variant="ghost"
            size="icon"
            className="size-11 rounded-lg text-muted-foreground hover:text-primary"
            aria-label="Открыть сохранённый материал"
            onClick={(event) => {
              event.stopPropagation();
              router.push(href);
            }}
          >
            <ChevronRight size={16} />
          </Button>
        </div>
      </div>
    </Card>
  );
}
