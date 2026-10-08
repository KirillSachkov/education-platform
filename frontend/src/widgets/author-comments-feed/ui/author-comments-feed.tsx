"use client";

import { useEffect, useState } from "react";
import type { RefCallback } from "react";
import { useSearchParams } from "next/navigation";
import { useInfiniteQuery } from "@tanstack/react-query";
import { useSession } from "next-auth/react";

import {
  authorFeedInfiniteQueryOptions,
  type AuthorFeedCommentDto,
} from "@/entities/comment";
import { CreateCommentInput, useMarkAuthorFeedViewed } from "@/features/comments";
import { EntityTypes } from "@/shared/config/entity-types";
import { getErrorMessage } from "@/shared/api";
import { formatRelativeDate } from "@/shared/lib/date";
import { cn } from "@/shared/lib/css";
import { UserAvatar } from "@/shared/ui/components";
import { Button } from "@/shared/ui/kit/button";
import { Tabs, TabsList, TabsTrigger } from "@/shared/ui/kit/tabs";
import { Icons } from "@/shared/ui/icons";

/**
 * Лента комментариев для автора — `/author/comments`.
 *
 * YouTube-Studio-style: все комментарии под контентом текущего автора в одной плоской
 * ленте, с фильтрами и inline reply. Источник правды — backend endpoint
 * `GET /comments/author-feed/`.
 */
type CommentsTab = "all" | "without_reply" | "unread";

const TAB_LABELS: Record<CommentsTab, string> = {
  all: "Все",
  without_reply: "Без ответа",
  unread: "Непрочитанные",
};

const TABS: CommentsTab[] = ["all", "without_reply", "unread"];

export function AuthorCommentsFeed() {
  const [tab, setTab] = useState<CommentsTab>("all");
  const searchParams = useSearchParams();
  const focusCommentId = searchParams.get("focus");
  const { markViewed } = useMarkAuthorFeedViewed();

  const filter = {
    limit: 20,
    withoutReply: tab === "without_reply",
    unreadOnly: tab === "unread",
  };

  const {
    data,
    isLoading,
    isError,
    error,
    refetch,
    fetchNextPage,
    hasNextPage,
    isFetchingNextPage,
  } = useInfiniteQuery(authorFeedInfiniteQueryOptions(filter));

  const items = data?.items ?? [];

  // На mount двигаем курсор «прочитано» — фоновая операция, не блокирует UI.
  // markViewed — `mutation.mutate`, новая ссылка на каждый рендер; намеренно ставим [],
  // чтобы фактический POST стрелял ровно один раз за монтирование страницы.
  useEffect(() => {
    markViewed();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return (
    <div className="max-w-3xl mx-auto px-4 sm:px-6 py-8 space-y-8">
      <header>
        <h1 className="text-2xl font-semibold tracking-tight">Комментарии</h1>
        <p className="text-sm text-muted-foreground mt-1">
          Все комментарии под вашими материалами. Отвечайте прямо здесь.
        </p>
      </header>

      <div className="overflow-x-auto -mx-1 px-1 [&::-webkit-scrollbar]:hidden [scrollbar-width:none]">
        <Tabs value={tab} onValueChange={(v) => setTab(v as CommentsTab)}>
          <TabsList>
            {TABS.map((key) => (
              <TabsTrigger key={key} value={key}>
                {TAB_LABELS[key]}
              </TabsTrigger>
            ))}
          </TabsList>
        </Tabs>
      </div>

      {isLoading ? (
        <div className="flex items-center justify-center py-20">
          <Icons.loading size={20} className="animate-spin text-muted-foreground" />
        </div>
      ) : isError ? (
        <div className="flex flex-col items-center justify-center py-20 text-center gap-3">
          <Icons.error size={20} className="text-destructive" />
          <p className="text-sm text-muted-foreground">
            {getErrorMessage(error, "Не удалось загрузить комментарии")}
          </p>
          <Button variant="outline" size="sm" onClick={() => refetch()}>
            Повторить
          </Button>
        </div>
      ) : items.length === 0 ? (
        <div className="flex flex-col items-center justify-center py-20 text-center gap-3">
          <Icons.comment size={28} className="text-muted-foreground/30" />
          <p className="text-sm text-muted-foreground">{emptyLabel(tab)}</p>
        </div>
      ) : (
        <ul className="space-y-2">
          {items.map((item) => (
            <li key={item.id}>
              <AuthorFeedRow item={item} isFocused={item.id === focusCommentId} />
            </li>
          ))}
        </ul>
      )}

      {hasNextPage && !isLoading && items.length > 0 && (
        <div className="flex justify-center pt-2">
          <Button
            variant="ghost"
            size="sm"
            onClick={() => fetchNextPage()}
            disabled={isFetchingNextPage}
            className="text-muted-foreground"
          >
            {isFetchingNextPage ? (
              <>
                <Icons.loading size={13} className="animate-spin mr-2" />
                Загружаем…
              </>
            ) : (
              "Показать ещё"
            )}
          </Button>
        </div>
      )}
    </div>
  );
}

function emptyLabel(tab: CommentsTab) {
  switch (tab) {
    case "without_reply":
      return "Все комментарии получили ответ";
    case "unread":
      return "Новых комментариев нет";
    default:
      return "Комментариев пока нет";
  }
}

function AuthorFeedRow({
  item,
  isFocused,
}: {
  item: AuthorFeedCommentDto;
  isFocused: boolean;
}) {
  const { data: session } = useSession();
  const [isReplying, setIsReplying] = useState(false);
  const displayName = item.authorName ?? item.authorUsername ?? "Пользователь";
  const isMaterial = item.targetEntityType === EntityTypes.MATERIAL;

  // Скролл и подсветка фокус-коммента: при первом mount, если карточка совпадает по id,
  // прокручиваем её в видимую область. Подсветка — статичный класс, без таймеров.
  const rowRef = useScrollIntoViewWhen(isFocused);

  return (
    <article
      ref={rowRef}
      id={`author-feed-${item.id}`}
      className={cn(
        "rounded-xl border border-border/50 bg-card p-4 transition-colors",
        isFocused && "border-primary/60 bg-primary/5",
        item.isUnread && !isFocused && "border-primary/30",
      )}
    >
      <div className="flex items-start gap-3">
        <UserAvatar
          name={displayName}
          avatarId={item.authorAvatarId}
          userId={item.authorId}
          className="size-9 shrink-0"
        />
        <div className="flex-1 min-w-0">
          <div className="flex items-center gap-2 text-sm">
            <span className="font-semibold text-foreground">{displayName}</span>
            <span className="text-muted-foreground/70">·</span>
            <time
              className="text-xs text-muted-foreground"
              dateTime={item.createdAt}
            >
              {formatRelativeDate(item.createdAt)}
            </time>
            {item.isUnread && (
              <span
                className="ml-auto inline-flex items-center gap-1 rounded-full bg-primary/10 px-2 py-0.5 text-[11px] font-medium text-primary"
                aria-label="Непрочитанный комментарий"
              >
                Новое
              </span>
            )}
          </div>

          <ContextLine item={item} isMaterial={isMaterial} />

          {item.parentPreview && (
            <blockquote className="mt-2 border-l-2 border-border/60 pl-2 text-xs text-muted-foreground line-clamp-2">
              {item.parentPreview}
            </blockquote>
          )}

          {item.isDeleted ? (
            <p className="mt-2 text-sm italic text-muted-foreground">
              Комментарий был удалён
            </p>
          ) : (
            <p className="mt-2 whitespace-pre-wrap break-words text-sm leading-6 text-foreground/90">
              {item.content}
            </p>
          )}

          {!item.isDeleted && (
            <div className="mt-3 flex items-center gap-2">
              <Button
                variant={isReplying ? "secondary" : "ghost"}
                size="sm"
                className="h-8"
                onClick={() => setIsReplying((v) => !v)}
              >
                {isReplying ? "Отмена" : item.hasMyReply ? "Ответить ещё" : "Ответить"}
              </Button>
              {item.hasMyReply && !isReplying && (
                <span className="inline-flex items-center gap-1 text-xs text-muted-foreground">
                  <Icons.check size={12} className="text-primary" />
                  Вы уже отвечали
                </span>
              )}
            </div>
          )}

          {isReplying && (
            <div className="mt-3">
              <CreateCommentInput
                targetEntity={{
                  type: item.targetEntityType,
                  id: item.targetEntityId,
                }}
                parentId={item.id}
                placeholder={`Ответ для ${displayName}…`}
                userName={session?.user?.displayName ?? session?.user?.name ?? null}
                userAvatarId={null}
                autoFocus
                onSuccess={() => setIsReplying(false)}
                onCancel={() => setIsReplying(false)}
              />
            </div>
          )}
        </div>
      </div>
    </article>
  );
}

function ContextLine({
  item,
  isMaterial,
}: {
  item: AuthorFeedCommentDto;
  isMaterial: boolean;
}) {
  const label = isMaterial ? "Материал" : item.targetEntityType;
  return (
    <div className="mt-1 text-xs text-muted-foreground/80">
      <span className="text-muted-foreground/60">{label}:</span>{" "}
      <span className="text-foreground/70">
        {item.targetTitle ?? "без названия"}
      </span>
    </div>
  );
}

/**
 * Скроллит элемент в видимую область, как только React монтирует ему ref.
 * Возвращает RefCallback'у, не useEffect — фокус-deeplink срабатывает один раз
 * на attach'е DOM-узла, не пересчитывается на каждом рендере.
 */
function useScrollIntoViewWhen(active: boolean): RefCallback<HTMLElement> {
  return (node) => {
    if (active && node !== null) {
      node.scrollIntoView({ behavior: "smooth", block: "center" });
    }
  };
}
