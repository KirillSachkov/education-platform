"use client";

import Link from "next/link";
import { useSearchParams } from "next/navigation";
import type { ComponentType, ReactNode } from "react";
import type { EntityType } from "@/shared/config/entity-types";
import { commentAncestorsQueryOptions } from "@/entities/comment";
import { profileQueryOptions } from "@/entities/profile";
import { getErrorMessage, isContentAccessError, isForbiddenError } from "@/shared/api";
import { useIsAuthenticated } from "@/shared/auth";
import { routes } from "@/shared/config/routes";
import { cn } from "@/shared/lib/css";
import { Button } from "@/shared/ui/kit/button";
import { useQuery } from "@tanstack/react-query";
import { Loader2, LockKeyhole, MessageSquare } from "lucide-react";

import { CommentNavigationProvider } from "../model/comment-navigation-context";
import { useRootsComment } from "../model/use-roots-comment";
import CommentCard from "./comment-card";
import { CreateCommentInput } from "./create-comment-input";

export interface CommentSectionProps {
  targetType: EntityType;
  targetId: string;
  className?: string;
}

interface CommentAccessStateProps {
  icon: ComponentType<{ className?: string }>;
  title: string;
  description: string;
  action?: ReactNode;
}

function CommentAccessState({ icon: Icon, title, description, action }: CommentAccessStateProps) {
  return (
    <div className="rounded-2xl border border-primary/15 bg-primary/5 px-5 py-6">
      <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
        <div className="flex items-start gap-4">
          <div className="flex size-11 shrink-0 items-center justify-center rounded-2xl bg-primary/12 text-primary">
            <Icon className="size-5" />
          </div>
          <div className="space-y-1">
            <p className="text-sm font-semibold text-foreground">{title}</p>
            <p className="text-sm leading-6 text-muted-foreground">{description}</p>
          </div>
        </div>

        {action ? <div className="sm:shrink-0">{action}</div> : null}
      </div>
    </div>
  );
}

export default function CommentSection({ targetType, targetId, className }: CommentSectionProps) {
  const isAuthenticated = useIsAuthenticated();
  const searchParams = useSearchParams();
  // ?focus=<commentId> — deep-link с виджета «Обсуждения» / уведомлений на конкретный
  // коммент. Подсветка и scrollIntoView срабатывают на первом attach'е DOM-узла,
  // даже если коммент сейчас скрыт под "Показать ответы" (сработает при раскрытии).
  const focusCommentId = searchParams.get("focus");
  const { data, isLoading, error, hasNextPage, isFetchingNextPage, cursorRef } = useRootsComment({
    targetType,
    targetId,
    enabled: isAuthenticated,
  });
  const { data: profile } = useQuery({
    ...profileQueryOptions.getMyProfileOptions(),
    enabled: isAuthenticated,
  });
  // Когда есть ?focus=<id> — заранее тянем ancestor chain. Каждый CommentCard,
  // чей id попадёт в forceExpandIds, раскроется автоматически → flat-thread
  // от depth=1 загрузит всех потомков → target замаунтится → существующий
  // pendingFocusCommentId-хук подсветит и проскроллит.
  const { data: ancestors } = useQuery({
    ...commentAncestorsQueryOptions(focusCommentId),
    enabled: isAuthenticated && !!focusCommentId,
  });
  const forceExpandIds = ancestors?.ancestorIds ?? null;

  const comments = data?.items ?? [];
  const totalCount = data?.totalCount ?? 0;
  const hasRestrictedAccess = !!error && (isForbiddenError(error) || isContentAccessError(error));

  return (
    <section className={cn("flex flex-col gap-4 sm:gap-5", className)}>
      <div className="flex items-center gap-2.5 sm:gap-3">
        <div className="flex size-8 items-center justify-center rounded-xl bg-primary/10 text-primary sm:size-9">
          <MessageSquare className="size-3.5 sm:size-4" />
        </div>
        <h2 className="text-base font-bold tracking-tight sm:text-lg">
          Обсуждение
          {totalCount > 0 && (
            <span className="ml-2 text-sm font-medium text-muted-foreground">{totalCount}</span>
          )}
        </h2>
      </div>

      {!isAuthenticated ? (
        <CommentAccessState
          icon={LockKeyhole}
          title="Обсуждение доступно после входа"
          description="Авторизуйтесь, чтобы читать комментарии и участвовать в обсуждении."
          action={
            <Button asChild size="sm" className="rounded-full">
              <Link href={routes.login}>Войти</Link>
            </Button>
          }
        />
      ) : hasRestrictedAccess ? (
        <CommentAccessState
          icon={MessageSquare}
          title="Обсуждение пока недоступно"
          description="Запишитесь на курс, чтобы открыть обсуждение и оставлять комментарии."
        />
      ) : (
        <CommentNavigationProvider
          pendingFocusCommentId={focusCommentId}
          forceExpandIds={forceExpandIds}
        >
          <CreateCommentInput
            targetEntity={{ type: targetType, id: targetId }}
            userName={profile?.name}
            userAvatarId={profile?.avatarId}
          />

          {isLoading ? (
            <div className="flex items-center justify-center rounded-2xl border border-border/50 bg-card/30 py-10">
              <Loader2 className="size-5 animate-spin text-muted-foreground" />
            </div>
          ) : error ? (
            <div className="rounded-2xl border border-destructive/20 bg-destructive/5 px-5 py-6 text-center">
              <p className="text-sm text-destructive">
                {getErrorMessage(error, "Ошибка загрузки комментариев")}
              </p>
            </div>
          ) : comments.length === 0 ? (
            <p className="px-1 py-2 text-xs text-muted-foreground/70 sm:text-sm">
              Комментариев пока нет. Будьте первым!
            </p>
          ) : (
            <div className="flex flex-col gap-1">
              {comments.map((comment) => (
                <CommentCard
                  key={comment.id}
                  comment={comment}
                  targetType={targetType}
                  targetId={targetId}
                  currentUserId={profile?.id}
                  currentUserName={profile?.displayName ?? profile?.name}
                  currentUserAvatarId={profile?.avatarId}
                />
              ))}
            </div>
          )}
        </CommentNavigationProvider>
      )}

      {isAuthenticated && !hasRestrictedAccess && hasNextPage && (
        <div ref={cursorRef} className="flex justify-center py-2">
          {isFetchingNextPage && <Loader2 className="h-5 w-5 animate-spin text-muted-foreground" />}
        </div>
      )}
    </section>
  );
}
