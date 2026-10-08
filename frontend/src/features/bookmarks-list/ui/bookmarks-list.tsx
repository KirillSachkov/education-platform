"use client";

import { AlertCircle, Loader2 } from "lucide-react";
import Link from "next/link";
import { usePathname } from "next/navigation";
import { getErrorMessage } from "@/shared/api";
import { useIsAuthenticated } from "@/shared/auth";
import { routes } from "@/shared/config/routes";
import { Button } from "@/shared/ui/kit/button";
import { Card, CardContent } from "@/shared/ui/kit/card";
import { Icons } from "@/shared/ui/icons";
import { useBookmarksList } from "../model/use-bookmarks-list";
import { BookmarkCard } from "./bookmark-card";
import { BookmarksEmptyState } from "./bookmarks-empty-state";

interface BookmarksListProps {
  courseId?: string;
}

export function BookmarksList({ courseId }: BookmarksListProps = {}) {
  const isAuthenticated = useIsAuthenticated();
  const pathname = usePathname();
  const {
    items,
    totalCount,
    hasNextPage,
    fetchNextPage,
    isLoading,
    isFetchingNextPage,
    error,
    refetch,
  } = useBookmarksList({ courseId, enabled: isAuthenticated });

  if (!isAuthenticated) {
    const loginHref = `${routes.login}?${new URLSearchParams({ callbackUrl: pathname ?? "/" }).toString()}`;
    return (
      <Card className="border-dashed">
        <CardContent className="flex flex-col items-center py-14 text-center gap-3">
          <div className="size-12 rounded-full bg-primary/10 border border-primary/20 flex items-center justify-center">
            <Icons.bookmark className="size-5 text-primary" />
          </div>
          <div className="space-y-1">
            <p className="text-base font-semibold">Закладки доступны после входа</p>
            <p className="text-sm text-muted-foreground">
              Войдите, чтобы сохранять материалы и быстро возвращаться к ним
            </p>
          </div>
          <Button asChild>
            <Link href={loginHref}>Войти</Link>
          </Button>
        </CardContent>
      </Card>
    );
  }

  if (isLoading) {
    return (
      <div className="flex justify-center py-20">
        <Loader2 size={24} className="animate-spin text-muted-foreground" />
      </div>
    );
  }

  if (error) {
    return (
      <Card className="p-6 gap-3">
        <div className="flex items-center gap-2 text-destructive">
          <AlertCircle size={16} />
          <span className="font-medium">Не удалось загрузить закладки</span>
        </div>
        <p className="text-sm text-muted-foreground">
          {getErrorMessage(error, "Попробуйте обновить страницу позже.")}
        </p>
        <div>
          <Button variant="outline" size="sm" onClick={() => refetch()}>
            Повторить
          </Button>
        </div>
      </Card>
    );
  }

  if (items.length === 0) {
    return <BookmarksEmptyState />;
  }

  return (
    <div className="space-y-4">
      <div className="text-sm text-muted-foreground">
        Всего в закладках: {totalCount}
      </div>

      <div className="space-y-3">
        {items.map((item) => (
          <BookmarkCard
            key={`${item.courseId}:${item.target.type}:${item.target.id}`}
            item={item}
          />
        ))}
      </div>

      {hasNextPage && (
        <div className="flex justify-center">
          <Button
            variant="outline"
            onClick={() => fetchNextPage()}
            disabled={isFetchingNextPage}
          >
            {isFetchingNextPage ? (
              <>
                <Loader2 size={14} className="animate-spin" />
                Загружаем...
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
