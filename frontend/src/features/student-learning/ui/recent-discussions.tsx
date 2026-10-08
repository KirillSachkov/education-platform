"use client";

import Link from "next/link";
import { useQuery } from "@tanstack/react-query";
import { commentInboxQueryOptions } from "@/entities/comment";
import { routes } from "@/shared/config/routes";
import { formatRelativeDate } from "@/shared/lib/date";
import { UserAvatar } from "@/shared/ui/components";
import { Card } from "@/shared/ui/kit/card";
import { Skeleton } from "@/shared/ui/kit/skeleton";
import { Icons } from "@/shared/ui/icons";

interface RecentDiscussionsProps {
  limit?: number;
}

const DEFAULT_LIMIT = 5;

export function RecentDiscussions({ limit = DEFAULT_LIMIT }: RecentDiscussionsProps) {
  const { data, isLoading, error } = useQuery(commentInboxQueryOptions(limit));

  if (isLoading) {
    return (
      <section className="space-y-2 sm:space-y-3">
        <SectionHeader />
        <div className="space-y-2">
          {[0, 1].map((i) => (
            <Skeleton key={i} className="h-[68px] rounded-xl" />
          ))}
        </div>
      </section>
    );
  }

  if (error || !data || data.length === 0) {
    return null;
  }

  return (
    <section className="space-y-2 sm:space-y-3">
      <SectionHeader />
      <div className="space-y-2">
        {data.map((item) => {
          // Material: course-scoped material view → standalone KB fallback.
          // Issue: course-scoped issue view; задачи всегда привязаны к курсу
          // через project, без targetCourseSlug ссылку не строим (orphan issue).
          // ?focus=<commentId> подсвечивает нужный коммент после загрузки
          // CommentSection на странице назначения.
          const focusQs = `?focus=${item.id}`;
          const href = (() => {
            if (item.targetEntityType === "material") {
              return item.targetCourseSlug
                ? `${routes.courseMaterial(item.targetCourseSlug, item.targetEntityId)}${focusQs}`
                : `${routes.materialDetail(item.targetEntityId)}${focusQs}`;
            }
            if (item.targetEntityType === "issue" && item.targetCourseSlug) {
              return `${routes.courseIssue(item.targetCourseSlug, item.targetEntityId)}${focusQs}`;
            }
            return null;
          })();

          const card = (
            <Card className="group p-3 sm:p-3.5 gap-0 border-border/60 hover:border-primary/40 transition-colors">
              <div className="flex items-start gap-2.5 sm:gap-3">
                <UserAvatar
                  name={item.authorName ?? item.authorUsername ?? "Аноним"}
                  avatarId={item.authorAvatarId}
                  userId={item.authorId}
                  className="size-8 sm:size-9 shrink-0"
                />
                <div className="min-w-0 flex-1">
                  <div className="flex items-center gap-1.5 text-[11px] sm:text-xs text-muted-foreground min-w-0">
                    <span className="truncate font-semibold text-foreground">
                      {item.authorName ?? item.authorUsername ?? "Аноним"}
                    </span>
                    <span className="text-border shrink-0">·</span>
                    <span className="shrink-0 tabular-nums">
                      {formatRelativeDate(item.createdAt)}
                    </span>
                  </div>
                  {item.content && (
                    <p className="text-xs sm:text-sm text-foreground/90 mt-0.5 line-clamp-2 leading-snug">
                      {item.content}
                    </p>
                  )}
                  {item.parentPreview && (
                    <p className="text-[11px] sm:text-xs text-muted-foreground/80 mt-1 line-clamp-1 italic">
                      <Icons.quote className="size-3 inline mr-1 align-[-1px]" />
                      {item.parentPreview}
                    </p>
                  )}
                </div>
                <Icons.arrowRight className="size-3.5 text-muted-foreground/40 shrink-0 mt-1.5 group-hover:text-primary group-hover:translate-x-0.5 transition-all" />
              </div>
            </Card>
          );

          return href ? (
            <Link key={item.id} href={href} prefetch={false} className="block">
              {card}
            </Link>
          ) : (
            <div key={item.id}>{card}</div>
          );
        })}
      </div>
    </section>
  );
}

function SectionHeader() {
  return (
    <div className="flex items-end justify-between gap-3">
      <div className="min-w-0">
        <h2 className="text-base sm:text-lg md:text-xl font-semibold tracking-tight">Обсуждения</h2>
        <p className="text-xs sm:text-sm text-muted-foreground mt-0.5">
          Недавние ответы на ваши комментарии
        </p>
      </div>
    </div>
  );
}
