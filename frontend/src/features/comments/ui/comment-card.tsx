"use client";

import { useState } from "react";
import { CornerUpLeft, Edit3, Loader2, MoreVertical, Trash2 } from "lucide-react";

import type { CommentDto } from "@/entities/comment";
import type { EntityType } from "@/shared/config/entity-types";
import { cn } from "@/shared/lib/css";
import { UserAvatar } from "@/shared/ui/components";
import { Button } from "@/shared/ui/kit/button";
import { formatRelativeDate } from "@/shared/lib/date";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/shared/ui/kit/dropdown-menu";

import { useCommentNavigation } from "../model/comment-navigation-context";
import { useChildrenComment } from "../model/use-children-comment";
import { useThreadComment } from "../model/use-thread-comment";
import { CreateCommentInput } from "./create-comment-input";
import { DeleteCommentDialog } from "./delete-comment-dialog";
import { UpdateCommentInput } from "./update-comment-input";

interface CommentCardProps {
  comment: CommentDto;
  targetType: EntityType;
  targetId: string;
  currentUserId?: string | null | undefined;
  currentUserName?: string | null | undefined;
  currentUserAvatarId?: string | null | undefined;
  depth?: number;
  isThreadItem?: boolean;
  threadPosition?: "single" | "first" | "middle" | "last";
  threadRootId?: string;
  onThreadReplyCreated?: () => void;
}

// Глубже этого уровня перестаём накапливать визуальный indent — все потомки
// рендерятся одной плоской лентой через `useThreadComment`. На depth=1 уже flat,
// один клик «Показать ответы» раскрывает всю ветку.
const MAX_COMPACT_DEPTH = 1;

function getUserDisplayName(name?: string | null, username?: string | null) {
  return name ?? username ?? "Пользователь";
}

function getCommentPreview(content: string) {
  return content.length > 110 ? `${content.slice(0, 107)}...` : content;
}

function pluralizeReplies(count: number) {
  const mod10 = count % 10;
  const mod100 = count % 100;
  if (mod10 === 1 && mod100 !== 11) return "ответ";
  if (mod10 >= 2 && mod10 <= 4 && (mod100 < 10 || mod100 >= 20)) return "ответа";
  return "ответов";
}

function ThreadConnector({ isLast }: { isLast: boolean }) {
  return (
    <div className="relative w-8 shrink-0 sm:w-10">
      <div className="absolute left-[16px] top-0 h-5 w-4 rounded-bl-2xl border-b border-l border-teal/80 sm:left-[20px] sm:w-5" />
      {!isLast && (
        <div className="absolute left-[16px] top-0 h-full w-0 border-l border-teal/80 sm:left-[20px]" />
      )}
    </div>
  );
}

function FlatThreadStartConnector() {
  return (
    <div className="pointer-events-none absolute left-0 top-0 h-5 w-8 sm:w-10">
      <div className="absolute left-[16px] top-0 h-5 w-4 rounded-bl-2xl border-b border-l border-teal/80 sm:left-[20px] sm:w-5" />
    </div>
  );
}

function ThreadReplyPreview({
  comment,
  threadRootId,
}: {
  comment: CommentDto;
  threadRootId?: string;
}) {
  const { focusComment } = useCommentNavigation();

  if (!comment.parentId || comment.parentId === threadRootId || !comment.parentPreview) {
    return null;
  }
  const parentId = comment.parentId;

  return (
    <button
      type="button"
      onClick={() => focusComment(parentId)}
      className="group mb-1.5 inline-flex max-w-full text-left align-top"
    >
      <span className="inline-flex max-w-[260px] items-center gap-1.5 rounded-md border border-border/50 bg-muted/30 px-2 py-1 text-[11px] leading-4 text-muted-foreground transition-colors duration-200 group-hover:bg-muted/50 group-hover:text-foreground/80">
        <span className="flex size-4 shrink-0 items-center justify-center rounded-sm bg-background/70 text-primary/80">
          <CornerUpLeft className="size-3" />
        </span>
        <span className="min-w-0">
          <span className="block truncate">{getCommentPreview(comment.parentPreview)}</span>
        </span>
      </span>
    </button>
  );
}

export default function CommentCard({
  comment,
  targetType,
  targetId,
  currentUserId,
  currentUserName,
  currentUserAvatarId,
  depth = 0,
  isThreadItem = false,
  threadPosition = "single",
  threadRootId,
  onThreadReplyCreated,
}: CommentCardProps) {
  const [isReplying, setIsReplying] = useState(false);
  // Root-level комменты раскрывают 1й уровень replies автоматически — для них
  // backend уже прислал preview-children в роуте /comments, лишних запросов нет.
  // Глубже (depth=1+ = flat-thread) — collapsed by default, чтобы не дёргать
  // useThreadComment массово при заходе на страницу.
  const { highlightedCommentId, registerCommentRef, forceExpandIds } = useCommentNavigation();
  const shouldForceExpand = !isThreadItem && forceExpandIds.includes(comment.id);
  const defaultRepliesOpen =
    shouldForceExpand ||
    (!isThreadItem && depth === 0 && (comment.previewChildren?.length ?? 0) > 0);
  // Derived state, чтобы deep-link forceExpandIds (приходящий асинхронно)
  // менял дефолт без setState-в-useEffect (React-Compiler-friendly).
  // Юзер может override'ить дефолт кнопкой «Скрыть»/«Показать» —
  // тогда userOpenOverride задаёт явный bool.
  const [userOpenOverride, setUserOpenOverride] = useState<boolean | null>(null);
  const areRepliesOpen = userOpenOverride ?? defaultRepliesOpen;
  const [isEditing, setIsEditing] = useState(false);
  const [isDeleteOpen, setIsDeleteOpen] = useState(false);
  const [hasLocalChildren, setHasLocalChildren] = useState(false);

  const shouldUseFlatThread = !isThreadItem && depth >= MAX_COMPACT_DEPTH;
  const compactThread = shouldUseFlatThread;
  const avatarSize = "size-8 sm:size-10";
  const avatarCenter = "left-[16px] sm:left-[20px]";
  const shouldShowReplyRail = !isThreadItem && areRepliesOpen;
  const showThreadRailAbove =
    isThreadItem && threadPosition !== "first" && threadPosition !== "single";
  const showThreadRailBelow =
    isThreadItem && threadPosition !== "last" && threadPosition !== "single";
  const isOwnComment = currentUserId?.trim().toLowerCase() === comment.authorId.toLowerCase();
  const displayName = getUserDisplayName(comment.authorName, comment.authorUsername);
  const isHighlighted = highlightedCommentId === comment.id;

  const {
    data: repliesData,
    isLoading: isLoadingReplies,
    refetch: refetchReplies,
    fetchNextPage: fetchNextRepliesPage,
    hasNextPage: hasNextRepliesPage,
    isFetchingNextPage: isFetchingNextRepliesPage,
  } = useChildrenComment({
    targetType,
    targetId,
    parentId: comment.id,
    enabled: areRepliesOpen && !shouldUseFlatThread && !isThreadItem,
    // Для root-комментов backend заинлайнил preview-children в /comments — сидим
    // первую страницу инфинит-кверя ими, чтобы не делать N+1 fetch'ей при открытии
    // страницы. fetchNextPage заработает только когда юзер нажмёт «Показать ещё».
    initialItems: comment.previewChildren ?? undefined,
    initialNextCursor: comment.previewChildrenNextCursor ?? undefined,
    initialTotalCount: comment.childrenCount,
  });

  const replies = repliesData?.items ?? comment.previewChildren ?? [];
  const remainingChildrenCount = Math.max(0, comment.childrenCount - replies.length);
  const {
    data: threadData,
    isLoading: isLoadingThread,
    refetch: refetchThread,
    fetchNextPage: fetchNextThreadPage,
    hasNextPage: hasNextThreadPage,
    isFetchingNextPage: isFetchingNextThreadPage,
  } = useThreadComment({
    targetType,
    targetId,
    parentId: comment.id,
    enabled: areRepliesOpen && shouldUseFlatThread,
  });

  const threadReplies = threadData?.items ?? [];
  const canShowReplies =
    !isThreadItem &&
    (comment.hasMoreChildren || hasLocalChildren || replies.length > 0 || threadReplies.length > 0);

  return (
    <article
      ref={registerCommentRef(comment.id)}
      id={`comment-${comment.id}`}
      className={cn(
        "group/comment relative flex flex-col rounded-xl transition-[background-color,box-shadow] duration-700",
        isHighlighted && "bg-primary/12",
      )}
    >
      <div className={cn("relative z-10 flex gap-2.5 sm:gap-3", compactThread && "gap-2")}>
        <div className={cn("relative shrink-0", "w-8 sm:w-10")}>
          <UserAvatar
            name={displayName}
            avatarId={comment.authorAvatarId}
            userId={comment.authorId}
            className={cn(avatarSize, "relative z-10 border-2 border-background bg-background")}
          />
          {shouldShowReplyRail && (
            <div
              className={cn(
                "absolute bottom-0 top-8 -z-10 w-0 border-l border-teal/80 sm:top-10",
                avatarCenter,
              )}
            />
          )}
          {showThreadRailAbove && (
            <div
              className={cn(
                "absolute top-0 -z-10 h-4 w-0 border-l border-teal/80 sm:h-5",
                avatarCenter,
              )}
            />
          )}
          {showThreadRailBelow && (
            <div
              className={cn(
                "absolute bottom-0 top-4 -z-10 w-0 border-l border-teal/80 sm:top-5",
                avatarCenter,
              )}
            />
          )}
        </div>

        <div className="min-w-0 flex-1 pb-3">
          <div className="flex min-w-0 items-start gap-2">
            <div className="min-w-0 flex-1">
              {isThreadItem && <ThreadReplyPreview comment={comment} threadRootId={threadRootId} />}

              <div className="mb-0.5 flex min-w-0 flex-wrap items-center gap-x-2 gap-y-0.5">
                <span className="truncate text-sm font-semibold leading-none">{displayName}</span>
                <span className="text-xs text-muted-foreground">
                  {formatRelativeDate(comment.createdAt)}
                </span>
                {comment.updatedAt !== comment.createdAt && !comment.isDeleted && (
                  <span className="text-[10px] text-muted-foreground">изменено</span>
                )}
              </div>

              {comment.isDeleted ? (
                <p className="mb-1.5 text-sm italic text-muted-foreground">
                  Комментарий был удалён
                </p>
              ) : isEditing ? (
                <div className="mb-1.5">
                  <UpdateCommentInput
                    commentId={comment.id}
                    defaultContent={comment.content}
                    onSuccess={() => setIsEditing(false)}
                    onCancel={() => setIsEditing(false)}
                  />
                </div>
              ) : (
                <p className="mb-1.5 whitespace-pre-wrap break-words text-sm leading-6 text-foreground/90">
                  {comment.content}
                </p>
              )}
            </div>

            <div className="flex size-8 shrink-0 justify-end">
              {isOwnComment && !comment.isDeleted && !isEditing && (
                <DropdownMenu>
                  <DropdownMenuTrigger asChild>
                    <Button
                      variant="ghost"
                      size="icon"
                      className="size-8 rounded-full text-muted-foreground hover:text-foreground"
                      aria-label="Действия с комментарием"
                    >
                      <MoreVertical className="size-4" />
                    </Button>
                  </DropdownMenuTrigger>
                  <DropdownMenuContent align="end" className="w-40">
                    <DropdownMenuItem onClick={() => setIsEditing(true)}>
                      <Edit3 className="size-4" />
                      Редактировать
                    </DropdownMenuItem>
                    <DropdownMenuItem variant="destructive" onSelect={() => setIsDeleteOpen(true)}>
                      <Trash2 className="size-4" />
                      Удалить
                    </DropdownMenuItem>
                  </DropdownMenuContent>
                </DropdownMenu>
              )}
            </div>
          </div>

          {!comment.isDeleted && !isEditing && (
            <div className="flex items-center gap-1">
              <Button
                variant="ghost"
                size="sm"
                className="h-7 rounded-full px-3 text-xs font-semibold text-muted-foreground hover:text-foreground"
                onClick={() => setIsReplying((v) => !v)}
              >
                Ответить
              </Button>
            </div>
          )}

          {isReplying && (
            <div className="mt-3">
              <CreateCommentInput
                targetEntity={{ type: targetType, id: targetId }}
                parentId={comment.id}
                placeholder={`Ответ для ${displayName}...`}
                userName={currentUserName}
                userAvatarId={currentUserAvatarId}
                autoFocus
                onSuccess={() => {
                  setIsReplying(false);
                  if (isThreadItem) {
                    onThreadReplyCreated?.();
                    return;
                  }

                  setHasLocalChildren(true);
                  setUserOpenOverride(true);
                  if (shouldUseFlatThread) {
                    void refetchThread();
                  } else {
                    void refetchReplies();
                  }
                }}
                onCancel={() => setIsReplying(false)}
              />
            </div>
          )}
        </div>
      </div>

      {(canShowReplies || areRepliesOpen) && (
        <div className="relative">
          {canShowReplies && (
            <div className="relative z-10 pb-3 pl-6 sm:pl-8">
              {shouldShowReplyRail && (
                <div
                  className={cn(
                    "pointer-events-none absolute inset-y-0 z-0 w-0 border-l border-teal/80",
                    avatarCenter,
                  )}
                />
              )}
              <Button
                variant="ghost"
                size="sm"
                className="h-8 rounded-full px-2 text-xs font-semibold text-primary hover:text-primary"
                onClick={() => setUserOpenOverride(!areRepliesOpen)}
              >
                {areRepliesOpen
                  ? "Скрыть"
                  : comment.childrenCount > 0
                    ? `Показать ${comment.childrenCount} ${pluralizeReplies(comment.childrenCount)}`
                    : "Показать ответы"}
              </Button>
            </div>
          )}

          {areRepliesOpen && shouldUseFlatThread && (
            <div className="relative z-10 mb-3 flex flex-col">
              {isLoadingThread ? (
                <div className="flex items-center gap-2 pb-3 pl-6 text-xs text-muted-foreground sm:pl-8">
                  <Loader2 className="size-4 animate-spin" />
                  <span>Загрузка...</span>
                </div>
              ) : (
                <div className="flex flex-col">
                  {threadReplies.map((reply, index) => (
                    <div key={reply.id} className="relative pl-8 sm:pl-10">
                      {index === 0 && <FlatThreadStartConnector />}
                      <div className="min-w-0 flex-1">
                        <CommentCard
                          comment={reply}
                          targetType={targetType}
                          targetId={targetId}
                          currentUserId={currentUserId}
                          currentUserName={currentUserName}
                          currentUserAvatarId={currentUserAvatarId}
                          depth={depth + 1}
                          isThreadItem
                          threadPosition={
                            threadReplies.length === 1
                              ? "single"
                              : index === 0
                                ? "first"
                                : index === threadReplies.length - 1
                                  ? "last"
                                  : "middle"
                          }
                          threadRootId={comment.id}
                          onThreadReplyCreated={() => {
                            setHasLocalChildren(true);
                            void refetchThread();
                          }}
                        />
                      </div>
                    </div>
                  ))}

                  {hasNextThreadPage && (
                    <Button
                      variant="ghost"
                      size="sm"
                      className="mt-1 h-8 w-fit rounded-full px-3 text-xs font-semibold text-primary hover:text-primary"
                      disabled={isFetchingNextThreadPage}
                      onClick={() => void fetchNextThreadPage()}
                    >
                      {isFetchingNextThreadPage && (
                        <Loader2 className="mr-1.5 size-4 animate-spin" />
                      )}
                      Показать ещё
                    </Button>
                  )}
                </div>
              )}
            </div>
          )}

          {areRepliesOpen && !shouldUseFlatThread && (
            <div className="relative z-10 flex flex-col">
              {isLoadingReplies && replies.length === 0 ? (
                <div className="flex items-center pb-3 pl-6 sm:pl-8">
                  <Loader2 className="mr-2 size-4 animate-spin text-muted-foreground" />
                  <span className="text-xs text-muted-foreground">Загрузка...</span>
                </div>
              ) : (
                <>
                  {replies.map((reply, index) => {
                    const isLastVisible = index === replies.length - 1 && !hasNextRepliesPage;
                    return (
                      <div key={reply.id} className="flex">
                        <ThreadConnector isLast={isLastVisible} />
                        <div className="min-w-0 flex-1 pt-1">
                          <CommentCard
                            comment={reply}
                            targetType={targetType}
                            targetId={targetId}
                            currentUserId={currentUserId}
                            currentUserName={currentUserName}
                            currentUserAvatarId={currentUserAvatarId}
                            depth={depth + 1}
                          />
                        </div>
                      </div>
                    );
                  })}

                  {hasNextRepliesPage && (
                    <div className="flex">
                      <ThreadConnector isLast />
                      <Button
                        variant="ghost"
                        size="sm"
                        className="mt-1 h-8 w-fit rounded-full px-3 text-xs font-semibold text-primary hover:text-primary"
                        disabled={isFetchingNextRepliesPage}
                        onClick={() => void fetchNextRepliesPage()}
                      >
                        {isFetchingNextRepliesPage && (
                          <Loader2 className="mr-1.5 size-4 animate-spin" />
                        )}
                        {remainingChildrenCount > 0
                          ? `Показать ещё ${remainingChildrenCount} ${pluralizeReplies(remainingChildrenCount)}`
                          : "Показать ещё"}
                      </Button>
                    </div>
                  )}
                </>
              )}
            </div>
          )}
        </div>
      )}

      <DeleteCommentDialog
        commentId={comment.id}
        open={isDeleteOpen}
        onOpenChange={setIsDeleteOpen}
      />
    </article>
  );
}
