"use client";

import Link from "next/link";
import { Icons } from "@/shared/ui/icons";
import { Badge } from "@/shared/ui/kit/badge";
import { Button } from "@/shared/ui/kit/button";
import { Card } from "@/shared/ui/kit/card";
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
import { Popover, PopoverContent, PopoverTrigger } from "@/shared/ui/kit/popover";
import {
  AuthorCredit,
  CompletionBadge,
  ContentImage,
  getCompletedTitleClass,
  ViewsBadge,
} from "@/shared/ui/components";
import { LockCallout } from "@/shared/ui/components/lock-callout";
import { cn } from "@/shared/lib/css";
import type { ReactNode } from "react";
import { routes } from "@/shared/config/routes";
import { resolveUnlockHref } from "@/shared/lib/lock-copy";
import { isRecentlyPublished } from "@/shared/lib/freshness";
import { formatRelativeDate } from "@/shared/lib/date";
import { formatDurationSeconds } from "@/shared/lib/duration";
import { getMaterialKindBadge, getMaterialStatusBadge } from "../lib/material-ui";
import type { MaterialFeedItemDto, MaterialLockReason, MaterialSummaryDto } from "../types";

/**
 * Card accepts either the rich feed DTO (with course/module/thumbnail/access context)
 * or the lighter summary DTO (listing view). Missing fields degrade gracefully.
 */
export type MaterialCardInput = MaterialFeedItemDto | MaterialSummaryDto;

export type MaterialCardVariant = "row" | "tile";

export interface MaterialCardProps {
  material: MaterialCardInput;
  variant?: MaterialCardVariant;
  /** Override navigation target. If omitted, derived from course context. */
  href?: string;
  /**
   * ISO timestamp of the user's view; when present, the card renders the
   * "Изучено DD.MM.YYYY" pill, strikes through the title, and hides the
   * "Новое" chip. `null`/`undefined` = not viewed.
   */
  viewedAt?: string | null;
  /** Render DRAFT / PUBLISHED / ARCHIVED status badge (author / teaching). */
  showStatus?: boolean;
  /**
   * Slot for the bookmark control. The card decides whether bookmarking is
   * applicable (not-locked + has courseId) and calls this with the bound
   * `courseId` and `materialId`; returning `null` hides the button. This keeps
   * the bookmark feature out of the entity layer (FSD: entity must not import
   * from features).
   */
  renderBookmark?: (args: {
    courseId: string;
    materialId: string;
    className?: string;
  }) => ReactNode;
  /** Render destructive delete action with confirmation dialog. */
  allowDelete?: boolean;
  onDelete?: (materialId: string) => void;
  isDeletePending?: boolean;
  /**
   * Optional highlighted match snippet (e.g. search `<mark>` markup) shown under
   * the title in the row layout. The card stays entity-layer — the caller passes
   * a pre-rendered node, so the feature-side highlight renderer isn't imported here.
   */
  highlightSnippet?: ReactNode;
}

function hasFeedFields(m: MaterialCardInput): m is MaterialFeedItemDto {
  return "isAccessible" in m;
}

export function MaterialCard({
  material,
  variant = "row",
  href: hrefOverride,
  viewedAt,
  showStatus = false,
  renderBookmark,
  allowDelete = false,
  onDelete,
  isDeletePending = false,
  highlightSnippet,
}: MaterialCardProps) {
  const kind = getMaterialKindBadge(material.kind);
  const KindIcon = kind.icon;
  const status = showStatus ? getMaterialStatusBadge(material.status) : null;

  const thumbnailUrl = "thumbnailUrl" in material ? (material.thumbnailUrl ?? null) : null;
  const moduleTitle = hasFeedFields(material) ? material.moduleTitle : null;
  const courseId = hasFeedFields(material) ? material.courseId : null;
  const courseTitle = hasFeedFields(material) ? material.courseTitle : null;
  const courseSlug = hasFeedFields(material) ? material.courseSlug : null;
  const isAccessible = hasFeedFields(material) ? material.isAccessible : true;
  const lockReason = hasFeedFields(material) ? material.lockReason : null;
  const viewsCount = material.viewsCount ?? 0;
  // Длительность видео (сек). Показываем только на VIDEO-карточках при наличии метаданных.
  const durationSeconds =
    material.kind === "VIDEO" && material.durationSeconds ? material.durationSeconds : null;

  const locked = !isAccessible;

  const derivedHref =
    hrefOverride ??
    (courseSlug
      ? routes.courseMaterial(courseSlug, material.id)
      : routes.materialDetail(material.id));

  if (variant === "tile") {
    return (
      <TileCard
        material={material}
        href={derivedHref}
        locked={locked}
        lockReason={lockReason}
        kind={kind}
        KindIcon={KindIcon}
        thumbnailUrl={thumbnailUrl}
        status={status}
        courseId={courseId}
        courseTitle={courseTitle}
        viewedAt={viewedAt}
        viewsCount={viewsCount}
        durationSeconds={durationSeconds}
        renderBookmark={renderBookmark}
      />
    );
  }

  return (
    <RowCard
      material={material}
      href={derivedHref}
      locked={locked}
      lockReason={lockReason}
      kind={kind}
      KindIcon={KindIcon}
      thumbnailUrl={thumbnailUrl}
      status={status}
      moduleTitle={moduleTitle}
      courseId={courseId}
      courseTitle={courseTitle}
      viewedAt={viewedAt}
      viewsCount={viewsCount}
      durationSeconds={durationSeconds}
      renderBookmark={renderBookmark}
      allowDelete={allowDelete}
      onDelete={onDelete}
      isDeletePending={isDeletePending}
      highlightSnippet={highlightSnippet}
    />
  );
}

type RowCardProps = {
  material: MaterialCardInput;
  href: string;
  locked: boolean;
  lockReason: MaterialLockReason | null | undefined;
  kind: ReturnType<typeof getMaterialKindBadge>;
  KindIcon: ReturnType<typeof getMaterialKindBadge>["icon"];
  thumbnailUrl: string | null | undefined;
  status: ReturnType<typeof getMaterialStatusBadge> | null;
  moduleTitle: string | null | undefined;
  courseId: string | null | undefined;
  courseTitle: string | null | undefined;
  viewedAt?: string | null;
  viewsCount: number;
  durationSeconds: number | null;
  renderBookmark?: (args: {
    courseId: string;
    materialId: string;
    className?: string;
  }) => ReactNode;
  allowDelete: boolean;
  onDelete?: (id: string) => void;
  isDeletePending: boolean;
  highlightSnippet?: ReactNode;
};

function RowCard({
  material,
  href,
  locked,
  lockReason,
  kind,
  KindIcon,
  thumbnailUrl,
  status,
  moduleTitle,
  courseId,
  courseTitle,
  viewedAt,
  viewsCount,
  durationSeconds,
  renderBookmark,
  allowDelete,
  onDelete,
  isDeletePending,
  highlightSnippet,
}: RowCardProps) {
  const recent = isRecentlyPublished(material.publishedAt);
  const hasMedia = Boolean(thumbnailUrl);
  const canNavigate = !locked && Boolean(href);
  const publishedDate = material.publishedAt ?? material.updatedAt;
  const isViewed = Boolean(viewedAt);
  const canBookmark = Boolean(renderBookmark) && !locked && Boolean(courseId);
  const isVideo = material.kind === "VIDEO";
  const hasActions = canBookmark || allowDelete;

  const body = (
    <Card
      className={cn(
        "group relative overflow-hidden border-border/60 p-0 gap-0 transition-colors duration-200",
        // Skip layout/paint for offscreen feed items. Each row has stable
        // intrinsic height (~110px on mobile, ~128px on desktop) so the
        // placeholder hint keeps the scrollbar stable during virtualization.
        "[content-visibility:auto] [contain-intrinsic-size:auto_136px]",
        canNavigate && "hover:border-primary/40 hover:bg-card/60",
        locked && "opacity-95",
      )}
    >
      <div className="flex min-h-[112px] flex-row items-stretch gap-3 p-2.5 sm:min-h-[128px] sm:p-3">
        {/* Media block — fixed size on all viewports. Same dimensions for every kind:
            thumbnail when present, kind-coloured fallback otherwise. */}
        <div className="shrink-0">
          {hasMedia ? (
            <div className="relative aspect-video w-24 overflow-hidden rounded-lg bg-muted sm:w-32 md:w-36">
              <ContentImage
                src={thumbnailUrl!}
                alt=""
                sizes="(min-width: 768px) 9rem, (min-width: 640px) 8rem, 6rem"
                className={cn(
                  // rounded-lg matches the wrapper radius: a filter:blur child escapes the
                  // parent's rounded overflow-hidden clip (square corners), so self-clip it.
                  "size-full rounded-lg object-cover transition-transform duration-300",
                  canNavigate && "group-hover:scale-[1.03]",
                  locked && "blur-[1px] opacity-70",
                )}
              />
              {isVideo && !locked && (
                <div className="absolute inset-0 flex items-center justify-center pointer-events-none">
                  <div className="size-6 sm:size-8 rounded-full bg-black/55 backdrop-blur-sm flex items-center justify-center">
                    <Icons.play className="size-2.5 sm:size-3.5 text-white fill-white ml-0.5" />
                  </div>
                </div>
              )}
              {locked && (
                <div className="absolute inset-0 bg-black/40 flex items-center justify-center">
                  <Icons.locked className="size-4 sm:size-5 text-white/90" />
                </div>
              )}
              {durationSeconds && (
                <span className="absolute bottom-1 right-1 rounded bg-black/60 px-1 py-px text-[10px] font-medium tabular-nums text-white backdrop-blur-sm">
                  {formatDurationSeconds(durationSeconds)}
                </span>
              )}
            </div>
          ) : (
            <div
              className={cn(
                "relative aspect-video w-24 overflow-hidden rounded-lg flex items-center justify-center sm:w-32 md:w-36",
                kind.iconBgClassName,
                locked && "opacity-70",
              )}
              aria-hidden="true"
            >
              <KindIcon className="size-7 sm:size-9 opacity-80" />
              {locked && (
                <div className="absolute inset-0 bg-background/40 flex items-center justify-center">
                  <Icons.locked className="size-4 sm:size-5 text-foreground/80" />
                </div>
              )}
              {durationSeconds && (
                <span className="absolute bottom-1 right-1 rounded bg-black/60 px-1 py-px text-[10px] font-medium tabular-nums text-white backdrop-blur-sm">
                  {formatDurationSeconds(durationSeconds)}
                </span>
              )}
            </div>
          )}
        </div>

        {/* Content column */}
        <div
          className={cn(
            "min-w-0 flex-1 flex flex-col gap-1 py-0.5 sm:gap-1.5",
            hasActions && "pr-8 sm:pr-9",
          )}
        >
          {/* Top row: kind chip + course breadcrumb */}
          <div className="flex min-w-0 items-center gap-1.5 overflow-hidden text-[10px] text-muted-foreground sm:text-[11px]">
            <span
              className={cn(
                "inline-flex items-center gap-1 rounded px-1.5 py-0.5 text-[9px] sm:text-[10px] font-semibold uppercase tracking-wide shrink-0",
                kind.iconBgClassName,
              )}
            >
              <KindIcon className="size-2.5 sm:size-3" />
              {kind.label}
            </span>
            {courseTitle ? (
              <span className="truncate min-w-0 text-foreground/80" title={courseTitle}>
                {courseTitle}
              </span>
            ) : courseId ? (
              <span className="truncate text-muted-foreground/80">Курс недоступен</span>
            ) : null}
            {moduleTitle && (
              <span className="hidden sm:inline-flex items-center gap-1 min-w-0">
                <Icons.chevronRight className="size-3 shrink-0 text-border" />
                <span className="truncate">{moduleTitle}</span>
              </span>
            )}
          </div>

          {/* Title */}
          <h3
            className={cn(
              "line-clamp-2 break-words text-sm font-semibold leading-snug text-foreground [overflow-wrap:anywhere] sm:text-base",
              canNavigate && !isViewed && "group-hover:text-primary transition-colors",
              getCompletedTitleClass(isViewed),
            )}
          >
            {material.title}
          </h3>

          {/* Search match snippet (highlighted) — only when caller supplies it. */}
          {highlightSnippet && (
            <div className="min-w-0 text-xs text-muted-foreground sm:text-[13px] [&_*]:break-words [&_*]:[overflow-wrap:anywhere] [&_p]:line-clamp-2">
              {highlightSnippet}
            </div>
          )}

          {/* Bottom meta: date + status badges */}
          <div className="mt-auto flex min-w-0 flex-wrap items-center gap-x-2 gap-y-1 pt-1 text-[10px] text-muted-foreground/80 sm:text-[11px]">
            {publishedDate && (
              <time dateTime={publishedDate} className="shrink-0 tabular-nums">
                {formatRelativeDate(publishedDate)}
              </time>
            )}
            {material.authorDisplayName && (
              <AuthorCredit
                name={material.authorDisplayName}
                avatarUrl={material.authorAvatarUrl}
                className="max-w-36 text-[10px] sm:max-w-44 sm:text-[11px]"
              />
            )}
            {isViewed && !locked && viewedAt && (
              <span
                className="inline-flex shrink-0 items-center gap-1 font-medium text-emerald-500"
                title={`Изучено ${new Date(viewedAt).toLocaleDateString("ru-RU")}`}
              >
                <Icons.completed className="size-3" />
                <span className="hidden sm:inline">Изучено</span>
              </span>
            )}
            {recent && !locked && !isViewed && (
              <span className="shrink-0 rounded border border-emerald-500/20 bg-emerald-500/10 px-1 py-px text-[9px] font-bold uppercase tracking-wider text-emerald-500 sm:text-[10px]">
                Новое
              </span>
            )}
            {locked && (
              <span className="inline-flex shrink-0 items-center gap-1 font-semibold">
                <Icons.locked className="size-3" />
                Закрыто
              </span>
            )}
            {status && (
              <Badge
                variant="outline"
                className={cn("h-4 shrink-0 px-1.5 text-[10px]", status.className)}
              >
                {status.label}
              </Badge>
            )}
            {viewsCount > 0 && (
              <ViewsBadge count={viewsCount} className="shrink-0 text-[10px] sm:text-[11px]" />
            )}
          </div>
        </div>

        {/* Actions: overlayed so dense metadata never steals horizontal room. */}
        {(canBookmark || allowDelete) && (
          <div
            className="absolute right-2.5 top-2.5 z-10 flex flex-col items-end justify-start gap-1"
            onClick={(e) => {
              e.stopPropagation();
              e.preventDefault();
            }}
          >
            {canBookmark && courseId && (
              <>
                {renderBookmark?.({
                  courseId,
                  materialId: material.id,
                  className: "size-7 sm:size-8",
                })}
              </>
            )}
            {allowDelete && (
              <AlertDialog>
                <AlertDialogTrigger asChild>
                  <Button
                    variant="ghost"
                    size="icon"
                    className="text-muted-foreground hover:bg-destructive/10 hover:text-destructive"
                    disabled={isDeletePending}
                    aria-label="Удалить материал"
                  >
                    <Icons.delete size={16} />
                  </Button>
                </AlertDialogTrigger>
                <AlertDialogContent>
                  <AlertDialogHeader>
                    <AlertDialogTitle>Удалить материал?</AlertDialogTitle>
                    <AlertDialogDescription>
                      Материал «{material.title}» будет безвозвратно удалён. Это действие нельзя
                      отменить.
                    </AlertDialogDescription>
                  </AlertDialogHeader>
                  <AlertDialogFooter>
                    <AlertDialogCancel>Отмена</AlertDialogCancel>
                    <AlertDialogAction
                      className="bg-destructive text-destructive-foreground hover:bg-destructive/90"
                      onClick={(e) => {
                        e.preventDefault();
                        onDelete?.(material.id);
                      }}
                    >
                      Удалить
                    </AlertDialogAction>
                  </AlertDialogFooter>
                </AlertDialogContent>
              </AlertDialog>
            )}
          </div>
        )}
      </div>
    </Card>
  );

  if (canNavigate) {
    return (
      <Link href={href} className="block" prefetch={false}>
        {body}
      </Link>
    );
  }

  if (locked) {
    return (
      <Popover>
        <PopoverTrigger asChild>
          <button type="button" className="block w-full text-left cursor-pointer">
            {body}
          </button>
        </PopoverTrigger>
        <PopoverContent
          align="start"
          sideOffset={8}
          collisionPadding={12}
          className="w-[min(20rem,calc(100vw-1.5rem))] rounded-2xl border-border/60 bg-card/95 p-4 shadow-xl shadow-black/20 backdrop-blur-xl"
        >
          <LockCallout
            reason={lockReason ?? null}
            courseTitle={courseTitle ?? null}
            ctaHref={resolveUnlockHref({
              lockReason,
              returnTo: typeof window !== "undefined" ? window.location.pathname : null,
            })}
          />
        </PopoverContent>
      </Popover>
    );
  }

  return body;
}

function TileCard({
  material,
  href,
  locked,
  lockReason,
  kind,
  KindIcon,
  thumbnailUrl,
  status,
  courseId,
  courseTitle,
  viewedAt,
  viewsCount,
  durationSeconds,
  renderBookmark,
}: {
  material: MaterialCardInput;
  href: string;
  locked: boolean;
  lockReason: MaterialLockReason | null | undefined;
  kind: ReturnType<typeof getMaterialKindBadge>;
  KindIcon: ReturnType<typeof getMaterialKindBadge>["icon"];
  thumbnailUrl: string | null | undefined;
  status: ReturnType<typeof getMaterialStatusBadge> | null;
  courseId: string | null | undefined;
  courseTitle: string | null | undefined;
  viewedAt?: string | null;
  viewsCount: number;
  durationSeconds: number | null;
  renderBookmark?: (args: {
    courseId: string;
    materialId: string;
    className?: string;
  }) => ReactNode;
}) {
  const recent = isRecentlyPublished(material.publishedAt);
  const canNavigate = !locked && Boolean(href);
  const publishedDate = material.publishedAt ?? material.updatedAt;
  const isViewed = Boolean(viewedAt);
  const canBookmark = Boolean(renderBookmark) && !locked && Boolean(courseId);

  const body = (
    <Card
      className={cn(
        "group relative overflow-hidden border-border/60 p-0 gap-0 transition-all duration-200 h-full",
        canNavigate && "hover:border-primary/40 hover:-translate-y-0.5 hover:shadow-lg",
        locked && "opacity-95",
      )}
    >
      <div className="relative aspect-video w-full overflow-hidden bg-muted">
        {thumbnailUrl ? (
          <>
            <ContentImage
              src={thumbnailUrl}
              alt=""
              sizes="(min-width: 1024px) 20rem, (min-width: 640px) 45vw, 100vw"
              className={cn(
                // rounded-t-2xl matches the card radius: a filter:blur child escapes the
                // card's rounded overflow-hidden clip (square top corners), so self-clip it.
                "size-full rounded-t-2xl object-cover transition-transform duration-300",
                canNavigate && "group-hover:scale-[1.04]",
                locked && "blur-[1px] opacity-70",
              )}
            />
            {material.kind === "VIDEO" && !locked && (
              <div className="absolute inset-0 flex items-center justify-center">
                <div className="size-12 rounded-full bg-black/55 backdrop-blur-sm flex items-center justify-center">
                  <Icons.play className="size-5 text-white fill-white ml-0.5" />
                </div>
              </div>
            )}
          </>
        ) : (
          <KindHeroBanner kind={kind} KindIcon={KindIcon} />
        )}
        {/* Bottom scrim so the overlaid title stays legible over any cover */}
        <div className="absolute inset-0 bg-gradient-to-t from-black/75 via-black/15 to-transparent" />
        {locked && (
          <div className="absolute inset-0 bg-black/40 flex items-center justify-center">
            <Icons.locked className="size-6 text-white/90" />
          </div>
        )}
        {/* Top-left kind chip */}
        <span
          className={cn(
            "absolute top-2 left-2 inline-flex items-center gap-1 rounded-md px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wide backdrop-blur-sm",
            kind.iconBgClassName,
          )}
        >
          <KindIcon className="size-3" />
          {kind.label}
        </span>
        {/* Top-right status chip */}
        {(recent || isViewed || locked) && (
          <div className="absolute top-2 right-2 flex items-center gap-1">
            {locked ? (
              <span className="inline-flex items-center gap-1 rounded-md bg-black/55 backdrop-blur-sm px-1.5 py-0.5 text-[10px] font-semibold text-white/90">
                <Icons.locked className="size-3" />
                Закрыто
              </span>
            ) : isViewed && viewedAt ? (
              <CompletionBadge
                kind="viewed"
                date={viewedAt}
                className="bg-emerald-500/95 dark:bg-emerald-500/95 border-emerald-500/40 text-white dark:text-white shadow-sm"
              />
            ) : recent ? (
              <span className="inline-flex items-center gap-1 rounded-md bg-emerald-500 px-1.5 py-0.5 text-[10px] font-bold uppercase tracking-wider text-white">
                Новое
              </span>
            ) : null}
          </div>
        )}
        {/* Bookmark — bottom-left corner of the media so it doesn't fight kind chip / view badge */}
        {canBookmark && courseId && (
          <div
            className="absolute bottom-2 right-2 z-10"
            onClick={(e) => {
              e.stopPropagation();
              e.preventDefault();
            }}
          >
            {renderBookmark?.({
              courseId,
              materialId: material.id,
              className:
                "size-8 bg-black/45 hover:bg-black/60 text-white border-white/20 backdrop-blur-sm",
            })}
          </div>
        )}
        {/* Title — overlaid on the cover, как на карточках планов/подборок */}
        <div className={cn("absolute inset-x-0 bottom-0 p-3", canBookmark && "pr-12")}>
          <h3
            className={cn(
              "line-clamp-2 text-sm sm:text-base font-semibold leading-snug drop-shadow-sm",
              isViewed ? "text-white/60 line-through" : "text-white",
            )}
          >
            {material.title}
          </h3>
        </div>
      </div>

      <div className="flex flex-col gap-1.5 p-3">
        <div className="flex items-center gap-1.5 text-[11px] text-muted-foreground min-w-0">
          {publishedDate && (
            <time dateTime={publishedDate} className="shrink-0 tabular-nums">
              {formatRelativeDate(publishedDate)}
            </time>
          )}
          {courseTitle && (
            <>
              <span className="text-border shrink-0">·</span>
              <span className="truncate">{courseTitle}</span>
            </>
          )}
          {status && (
            <>
              <span className="text-border shrink-0">·</span>
              <Badge variant="outline" className={cn("h-4 px-1.5 text-[10px]", status.className)}>
                {status.label}
              </Badge>
            </>
          )}
          {durationSeconds && (
            <>
              <span className="text-border shrink-0">·</span>
              <span className="inline-flex items-center gap-1 tabular-nums">
                <Icons.clock className="size-3" />
                {formatDurationSeconds(durationSeconds)}
              </span>
            </>
          )}
          {viewsCount > 0 && (
            <>
              <span className="text-border shrink-0">·</span>
              <ViewsBadge count={viewsCount} className="text-[11px]" />
            </>
          )}
        </div>
      </div>
    </Card>
  );

  if (canNavigate) {
    return (
      <Link href={href} className="block h-full" prefetch={false}>
        {body}
      </Link>
    );
  }

  if (locked) {
    return (
      <Popover>
        <PopoverTrigger asChild>
          <button type="button" className="block w-full h-full text-left cursor-pointer">
            {body}
          </button>
        </PopoverTrigger>
        <PopoverContent
          align="start"
          sideOffset={8}
          collisionPadding={12}
          className="w-[min(20rem,calc(100vw-1.5rem))] rounded-2xl border-border/60 bg-card/95 p-4 shadow-xl shadow-black/20 backdrop-blur-xl"
        >
          <LockCallout
            reason={lockReason ?? null}
            courseTitle={courseTitle ?? null}
            ctaHref={resolveUnlockHref({
              lockReason,
              returnTo: typeof window !== "undefined" ? window.location.pathname : null,
            })}
          />
        </PopoverContent>
      </Popover>
    );
  }

  return body;
}

function KindHeroBanner({
  kind,
  KindIcon,
}: {
  kind: ReturnType<typeof getMaterialKindBadge>;
  KindIcon: ReturnType<typeof getMaterialKindBadge>["icon"];
}) {
  // Tile hero: centered kind icon — the title now lives in the bottom overlay.
  return (
    <div
      className={cn(
        "relative flex flex-col items-center justify-center size-full px-4",
        kind.iconBgClassName,
      )}
      aria-hidden="true"
    >
      <KindIcon className="size-12" />
    </div>
  );
}
