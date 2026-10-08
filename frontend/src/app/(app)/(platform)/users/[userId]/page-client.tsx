"use client";

import { useQuery } from "@tanstack/react-query";
import { Github, Loader2, Sparkles, Trophy } from "lucide-react";
import { publicProfileQueryOptions } from "@/entities/profile";
import { userProgressQueryOptions } from "@/entities/user-progress";
import { authorCoursesQueryOptions, CourseCatalogCard } from "@/entities/course";
import { UserAvatar } from "@/shared/ui/components/user-avatar";
import { NotFoundFallback } from "@/shared/ui/components/not-found-fallback";
import { routes } from "@/shared/config/routes";
import { Separator } from "@/shared/ui/kit/separator";

export function UserProfileClient({ userId }: { userId: string }) {
  const { data: profile, isLoading } = useQuery(publicProfileQueryOptions(userId));
  const { data: xp } = useQuery(userProgressQueryOptions.getUserXpProgressOptions(userId));
  // Курсы автора (#637) — опубликованные курсы пользователя. Если он не автор /
  // курсов нет — `items` пуст и секция скрывается. Анонимам доступно (endpoint public).
  const { data: authorCourses } = useQuery(authorCoursesQueryOptions(userId));
  const courses = authorCourses?.items ?? [];

  if (isLoading) {
    return (
      <div className="flex items-center justify-center h-full">
        <Loader2 className="size-6 animate-spin text-muted-foreground" />
      </div>
    );
  }

  if (!profile) {
    return (
      <NotFoundFallback
        message="Пользователь не найден"
        backHref={routes.home}
        backLabel="На главную"
      />
    );
  }

  const displayName = profile.displayName ?? profile.username ?? "Пользователь";
  const hasBio = profile.bio || profile.aboutAsAuthor;
  const hasLinks = !!profile.gitHubUrl;

  return (
    <div className="h-full overflow-y-auto">
      <div className="max-w-2xl mx-auto px-4 md:px-8 py-6 md:py-10 space-y-6">
        {/* Header */}
        <div className="flex flex-col sm:flex-row items-start gap-4 sm:gap-6">
          <UserAvatar
            name={displayName}
            avatarId={profile.avatarId}
            className="size-20 text-2xl shrink-0"
          />
          <div className="min-w-0 flex-1">
            <h1 className="text-2xl font-bold break-words">{displayName}</h1>
            {profile.username && (
              <p className="text-sm text-muted-foreground mt-0.5">@{profile.username}</p>
            )}
            {profile.specialization && (
              <p className="text-sm text-primary mt-1">{profile.specialization}</p>
            )}

            {hasLinks && (
              <div className="flex flex-wrap items-center gap-3 mt-3">
                {profile.gitHubUrl && (
                  <a
                    href={profile.gitHubUrl}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="inline-flex items-center gap-1.5 text-sm text-muted-foreground hover:text-foreground transition-colors"
                  >
                    <Github size={15} className="shrink-0" />
                    <span className="truncate max-w-[140px] sm:max-w-none">
                      {profile.gitHubUrl.replace("https://github.com/", "")}
                    </span>
                  </a>
                )}
              </div>
            )}
          </div>
        </div>

        {/* Level + XP — публичная статистика */}
        {xp && (
          <div className="rounded-2xl border border-border/50 bg-card/40 p-4 sm:p-5">
            <div className="flex items-center gap-4">
              <div className="flex size-12 shrink-0 items-center justify-center rounded-xl bg-gold/15 border border-gold/25 text-gold font-bold">
                {xp.currentLevel}
              </div>
              <div className="min-w-0 flex-1">
                <div className="flex items-center gap-2">
                  <Sparkles size={14} className="text-primary shrink-0" />
                  <p className="text-sm font-medium text-foreground">Уровень {xp.currentLevel}</p>
                </div>
                <p className="mt-1 text-xs text-muted-foreground inline-flex items-center gap-1.5">
                  <Trophy size={12} className="text-gold" />
                  <span className="tabular-nums">{xp.totalXp.toLocaleString()} XP</span>
                  {xp.xpToNextLevel !== null && xp.nextLevel !== null && (
                    <>
                      <span className="text-border">·</span>
                      <span className="tabular-nums">
                        ещё {xp.xpToNextLevel.toLocaleString()} до уровня {xp.nextLevel}
                      </span>
                    </>
                  )}
                </p>
              </div>
            </div>
          </div>
        )}

        {hasBio && (
          <>
            <Separator />
            <div>
              <h2 className="text-xs font-semibold uppercase tracking-wider text-muted-foreground mb-3">
                О себе
              </h2>
              <p className="text-sm leading-relaxed text-foreground/80 whitespace-pre-wrap break-words">
                {profile.aboutAsAuthor ?? profile.bio}
              </p>
            </div>
          </>
        )}

        {profile.aboutAsAuthor && profile.bio && profile.bio !== profile.aboutAsAuthor && (
          <>
            <Separator />
            <div>
              <h2 className="text-xs font-semibold uppercase tracking-wider text-muted-foreground mb-3">
                Биография
              </h2>
              <p className="text-sm leading-relaxed text-foreground/80 whitespace-pre-wrap break-words">
                {profile.bio}
              </p>
            </div>
          </>
        )}

        {courses.length > 0 && (
          <>
            <Separator />
            <div>
              <h2 className="text-xs font-semibold uppercase tracking-wider text-muted-foreground mb-3">
                Курсы
              </h2>
              <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                {courses.map((course, index) => (
                  <CourseCatalogCard key={course.id} course={course} priority={index < 2} />
                ))}
              </div>
            </div>
          </>
        )}

        {!hasBio && !hasLinks && !profile.specialization && !xp && courses.length === 0 && (
          <div className="py-8 text-center">
            <p className="text-sm text-muted-foreground">Пользователь пока не заполнил профиль</p>
          </div>
        )}
      </div>
    </div>
  );
}
