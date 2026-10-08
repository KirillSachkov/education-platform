"use client";

import {
  collectionDetailQueryOptions,
  CollectionCoverImage,
  getCollectionGradient,
  type CollectionItemDto,
  type CollectionLockReason,
  type CollectionSectionDto,
} from "@/entities/collection";
import { getMaterialKindBadge, type MaterialSummaryDto } from "@/entities/material";
import { userProgressQueryOptions } from "@/entities/user-progress";
import { BookmarkStatusProvider } from "@/entities/bookmark";
import { routes } from "@/shared/config/routes";
import { cn } from "@/shared/lib/css";
import { formatNumericDate, formatRelativeDate } from "@/shared/lib/date";
import {
  resolveLockCopy,
  resolveUnlockHref,
  resolveSecondaryUnlockHref,
} from "@/shared/lib/lock-copy";
import { Icons } from "@/shared/ui/icons";
import { Card, CardContent } from "@/shared/ui/kit/card";
import { ErrorCard } from "@/shared/ui/kit/error-card";
import { Skeleton } from "@/shared/ui/kit/skeleton";
import { LockCallout } from "@/shared/ui/components/lock-callout";
import { Popover, PopoverContent, PopoverTrigger } from "@/shared/ui/kit/popover";
import { ContentImage, CourseBreadcrumb, ShareButton } from "@/shared/ui/components";
import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { usePathname } from "next/navigation";
import type { ReactNode } from "react";
import { useSession } from "next-auth/react";

interface CollectionDetailViewProps {
  collectionId: string;
  /** Resolves a material item to a link */
  getMaterialHref: (materialId: string) => string;
  /** URL назад в базу знаний — рендерится как «← К базе знаний» в шапке. */
  backHref?: string;
  backLabel?: string;
  /**
   * Optional bookmark slot — page-level callers inject the BookmarkToggleButton
   * (kept out of the entity layer to satisfy FSD layering). When omitted, no
   * bookmark control is shown (e.g. space-level views without course context).
   */
  renderBookmark?: (args: {
    courseId: string;
    materialId: string;
    className?: string;
  }) => ReactNode;
}

export function CollectionDetailView({
  collectionId,
  getMaterialHref,
  backHref,
  backLabel = "К базе знаний",
  renderBookmark,
}: CollectionDetailViewProps) {
  const {
    data: collection,
    isLoading,
    error,
  } = useQuery(collectionDetailQueryOptions(collectionId));

  const session = useSession();
  const isAuthenticated = session.status === "authenticated";
  // QUIZ-items (#491) не участвуют в материальном прогрессе/закладках — счётчики
  // и viewed-статусы считаем только по MATERIAL-items.
  const materialItems =
    collection?.sections.flatMap((s) =>
      s.items.filter((i) => i.itemType !== "QUIZ" && i.material != null),
    ) ?? [];
  const materialIds = materialItems.map((i) => i.referenceId);
  const { data: viewedMap } = useQuery({
    ...userProgressQueryOptions.materialViewStatusOptions(materialIds),
    enabled: isAuthenticated && materialIds.length > 0,
  });

  if (isLoading) {
    return (
      <div className="mx-auto max-w-4xl space-y-6 p-4 sm:p-6">
        <Skeleton className="h-32 rounded-2xl" />
        <div className="space-y-3">
          <Skeleton className="h-24 rounded-xl" />
          <Skeleton className="h-24 rounded-xl" />
          <Skeleton className="h-24 rounded-xl" />
        </div>
      </div>
    );
  }

  // Detail-эндпоинт больше не отдаёт 401/403 для PUBLISHED-подборок: даже гейтнутая
  // подборка открывается с per-item замками и `isAccessible/lockReason` на header'е.
  // Здесь оставлена обработка только реальных ошибок (network, 404 и т.п.).
  if (error) {
    return <ErrorCard error={error} className="py-16" />;
  }

  if (!collection) {
    return (
      <div className="flex flex-col items-center justify-center py-16 text-center text-muted-foreground">
        <p>Подборка не найдена</p>
      </div>
    );
  }

  const totalItems = materialItems.length;
  const viewedCount = materialItems.filter(
    (item) => viewedMap?.get(item.referenceId)?.isViewed,
  ).length;
  const countLabel = totalItems === 1 ? "материал" : totalItems < 5 ? "материала" : "материалов";

  // Цепочку строим параллельно course-material-view / issue-view: в course-контексте
  // immediately-preceding линк — сам курс (без промежуточного «База знаний»), чтобы
  // возвращение назад по breadcrumb'у вело на курс, а не в KB. В space-контексте
  // (collection.courseSlug == null) родителем остаётся «База знаний» автора — у
  // space-level подборки нет курсового шага.
  const breadcrumbs = [
    ...(collection.courseSlug && collection.courseTitle
      ? [
          {
            label: collection.courseTitle,
            href: routes.courseOverview(collection.courseSlug),
          },
        ]
      : [{ label: "База знаний", href: routes.knowledgeBase }]),
    { label: collection.title },
  ];

  const collectionShareHref = collection.courseSlug
    ? routes.courseCollectionDetail(collection.courseSlug, collectionId)
    : routes.collectionDetail(collectionId);

  return (
    <div className="mx-auto max-w-4xl p-4 sm:p-6 space-y-8">
      <div className="flex items-center justify-between gap-3">
        <div className="min-w-0 overflow-x-auto">
          <CourseBreadcrumb items={breadcrumbs} />
        </div>
        <ShareButton url={collectionShareHref} title={collection.title} className="shrink-0" />
      </div>
      {!breadcrumbs.length && backHref ? (
        <Link
          href={backHref}
          className="inline-flex items-center gap-1.5 text-sm text-muted-foreground transition-colors hover:text-foreground"
        >
          <span aria-hidden>«</span>
          <span>{backLabel}</span>
        </Link>
      ) : null}

      <CollectionHeaderCard
        collection={collection}
        totalItems={totalItems}
        countLabel={countLabel}
        viewedCount={viewedCount}
        showProgress={isAuthenticated && totalItems > 0}
        courseHref={collection.courseSlug ? routes.courseOverview(collection.courseSlug) : null}
      />

      {!collection.isAccessible && collection.lockReason && (
        <CollectionHeaderLockNotice
          lockReason={collection.lockReason}
          courseTitle={collection.courseTitle}
        />
      )}

      {collection.sections.length === 0 ? (
        <Card>
          <CardContent className="py-10 text-center text-muted-foreground">
            Подборка пока пуста
          </CardContent>
        </Card>
      ) : (
        <BookmarkStatusProvider
          items={
            isAuthenticated && collection.courseId
              ? materialItems.map((i) => ({
                  courseId: collection.courseId!,
                  entityType: "Material" as const,
                  entityId: i.referenceId,
                }))
              : []
          }
        >
          <div className="space-y-10">
            {collection.sections.map((section, sectionIndex) => (
              <CollectionSection
                key={section.id}
                section={section}
                sectionNumber={sectionIndex + 1}
                showSectionHeader={collection.sections.length > 1 || !!section.title}
                courseContext={{
                  courseId: collection.courseId,
                  courseTitle: collection.courseTitle,
                  courseSlug: collection.courseSlug,
                }}
                getMaterialHref={getMaterialHref}
                viewedMap={viewedMap}
                renderBookmark={renderBookmark}
              />
            ))}
          </div>
        </BookmarkStatusProvider>
      )}
    </div>
  );
}

function CollectionHeaderCard({
  collection,
  totalItems,
  countLabel,
  viewedCount,
  showProgress,
  courseHref,
}: {
  collection: {
    id: string;
    title: string;
    description: string | null;
    coverImageUrl: string | null;
    courseTitle?: string | null;
  };
  totalItems: number;
  countLabel: string;
  viewedCount: number;
  showProgress: boolean;
  courseHref: string | null;
}) {
  const percent = totalItems > 0 ? Math.round((viewedCount / totalItems) * 100) : 0;

  return (
    <div className="overflow-hidden rounded-2xl border border-border/60 bg-card shadow-sm">
      <div className="flex flex-col gap-4 p-5 sm:flex-row sm:gap-5 sm:p-6">
        {/* Compact 3:4 portrait thumbnail — left on desktop, top-left on mobile */}
        <div className="relative aspect-[3/4] w-40 shrink-0 overflow-hidden rounded-xl bg-muted/40 sm:w-44">
          <CollectionCoverImage
            src={collection.coverImageUrl}
            alt=""
            fill
            sizes="176px"
            className="object-cover"
            priority
            fallback={
              <div
                className={cn(
                  "absolute inset-0 flex items-center justify-center bg-gradient-to-br",
                  getCollectionGradient(collection.id),
                )}
              >
                <Icons.layers className="size-10 text-white/35" aria-hidden />
              </div>
            }
          />
        </div>

        <div className="min-w-0 flex-1 flex flex-col gap-2">
          {collection.courseTitle &&
            (courseHref ? (
              <Link
                href={courseHref}
                prefetch={false}
                className="inline-flex w-fit items-center gap-1 rounded-md bg-muted/60 px-2 py-0.5 text-[11px] font-medium text-muted-foreground transition-colors hover:bg-muted hover:text-foreground"
              >
                <Icons.course className="size-3" />
                {collection.courseTitle}
              </Link>
            ) : (
              <span className="inline-flex w-fit items-center gap-1 rounded-md bg-muted/60 px-2 py-0.5 text-[11px] font-medium text-muted-foreground">
                <Icons.course className="size-3" />
                {collection.courseTitle}
              </span>
            ))}
          <h1 className="text-xl font-bold leading-tight sm:text-2xl">{collection.title}</h1>
          {collection.description && (
            <p className="text-sm text-muted-foreground leading-relaxed">
              {collection.description}
            </p>
          )}

          {/* Counter row — count on the left, percent on the right, then a thin progress bar below */}
          <div className="mt-auto pt-2">
            <div className="flex items-center justify-between text-xs">
              <span className="inline-flex items-center gap-1.5 text-muted-foreground">
                <Icons.layers size={13} />
                {totalItems} {countLabel}
              </span>
              {showProgress && (
                <span className="tabular-nums font-semibold text-primary">{percent}% изучено</span>
              )}
            </div>
            {showProgress && (
              <div className="mt-2 h-1 w-full overflow-hidden rounded-full bg-muted">
                <div
                  className="h-full rounded-full bg-primary transition-[width] duration-500"
                  style={{ width: `${percent}%` }}
                />
              </div>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}

function CollectionSection({
  section,
  sectionNumber,
  showSectionHeader,
  courseContext,
  getMaterialHref,
  viewedMap,
  renderBookmark,
}: {
  section: CollectionSectionDto;
  sectionNumber: number;
  showSectionHeader: boolean;
  courseContext: {
    courseId: string | null;
    courseTitle: string | null;
    courseSlug: string | null;
  };
  getMaterialHref: (materialId: string) => string;
  viewedMap?: Map<string, { isViewed: boolean; viewedAt: string | null }>;
  renderBookmark?: (args: {
    courseId: string;
    materialId: string;
    className?: string;
  }) => ReactNode;
}) {
  return (
    <section className="space-y-3">
      {showSectionHeader && section.title && (
        <h2 className="text-lg font-semibold sm:text-xl">
          {sectionNumber}. {section.title}
        </h2>
      )}
      {section.description && (
        <p className="text-sm text-muted-foreground">{section.description}</p>
      )}
      {section.items.length === 0 ? (
        <p className="py-2 text-sm text-muted-foreground/70 italic">
          В этой секции пока нет материалов
        </p>
      ) : (
        <ol className="rounded-2xl border border-border/60 bg-card/40 px-2 py-1.5 sm:px-3 sm:py-2">
          {section.items.map((item, itemIndex) =>
            item.itemType !== "QUIZ" && item.material != null ? (
              <CollectionTimelineRow
                key={item.id}
                item={item}
                material={item.material}
                prefix={`${sectionNumber}.${itemIndex + 1}`}
                isFirst={itemIndex === 0}
                isLast={itemIndex === section.items.length - 1}
                courseId={courseContext.courseId}
                courseTitle={courseContext.courseTitle}
                href={getMaterialHref(item.referenceId)}
                viewedAt={viewedMap?.get(item.referenceId)?.viewedAt ?? null}
                renderBookmark={renderBookmark}
              />
            ) : (
              <CollectionQuizTimelineRow
                key={item.id}
                item={item}
                prefix={`${sectionNumber}.${itemIndex + 1}`}
                isFirst={itemIndex === 0}
                isLast={itemIndex === section.items.length - 1}
                courseTitle={courseContext.courseTitle}
              />
            ),
          )}
        </ol>
      )}
    </section>
  );
}

/**
 * Обёртка ряда: открытый item — обычная ссылка на материал; закрытый — кнопка,
 * по клику показывающая замок-диалог (LockCallout) на месте, без перехода на
 * /login. Единый паттерн с программой курса (course-curriculum). См. #385.
 */
function RowLink({
  isLocked,
  href,
  lockReason,
  courseTitle,
  unlockHref,
  secondaryHref,
  children,
}: {
  isLocked: boolean;
  href: string;
  lockReason: CollectionLockReason | null;
  courseTitle: string | null;
  unlockHref: string | null;
  secondaryHref: string | null;
  children: ReactNode;
}) {
  const className = "flex min-w-0 flex-1 items-start gap-3 rounded-sm focus-visible:outline-none";
  if (!isLocked) {
    return (
      <Link href={href} className={className}>
        {children}
      </Link>
    );
  }
  return (
    <Popover>
      <PopoverTrigger asChild>
        <button type="button" className={cn(className, "cursor-pointer text-left")}>
          {children}
        </button>
      </PopoverTrigger>
      <PopoverContent
        align="start"
        sideOffset={8}
        collisionPadding={12}
        className="w-[min(20rem,calc(100vw-1.5rem))] rounded-2xl border-border/60 bg-card/95 p-4 shadow-xl shadow-black/20 backdrop-blur-xl"
      >
        <LockCallout
          reason={lockReason}
          courseTitle={courseTitle}
          ctaHref={unlockHref}
          secondaryCtaHref={secondaryHref}
        />
      </PopoverContent>
    </Popover>
  );
}

/**
 * Timeline-итем подборки.
 *
 * Ряд = слева вертикальная "рельса" (точка-маркер статуса прогресса),
 * затем компактная 16:9 тумба (preview/video-thumbnail или kind-fallback),
 * справа kind-бейдж + опциональные плашки (Изучено / Закрыто), заголовок,
 * мета-строка с датой публикации и кнопка-закладка.
 */
function CollectionTimelineRow({
  item,
  material,
  prefix,
  isFirst,
  isLast,
  courseId,
  courseTitle,
  href,
  viewedAt,
  renderBookmark,
}: {
  item: CollectionItemDto;
  /** Non-null карточка материала — caller гарантирует item.itemType=MATERIAL. */
  material: MaterialSummaryDto;
  prefix: string;
  isFirst: boolean;
  isLast: boolean;
  courseId: string | null;
  courseTitle: string | null;
  href: string;
  viewedAt: string | null;
  renderBookmark?: (args: {
    courseId: string;
    materialId: string;
    className?: string;
  }) => ReactNode;
}) {
  const kindBadge = getMaterialKindBadge(material.kind);
  const KindIcon = kindBadge.icon;
  const isViewed = !!viewedAt;
  const isLocked = !item.isAccessible;
  const lockCopy = isLocked ? resolveLockCopy(item.lockReason, courseTitle) : null;
  // Закрытый item НЕ ведёт на /login — по клику показываем замок-диалог на месте
  // (как в программе курса), без редиректа (#385). Открытый — обычная ссылка.
  const returnTo = usePathname();
  const unlockHref = isLocked ? resolveUnlockHref({ lockReason: item.lockReason, returnTo }) : null;
  const secondaryHref = isLocked
    ? resolveSecondaryUnlockHref({ lockReason: item.lockReason })
    : null;
  const thumbnailUrl = material.thumbnailUrl ?? null;
  const isVideo = material.kind === "VIDEO";
  const publishedDate = material.publishedAt ?? material.updatedAt;

  return (
    <li className="relative grid grid-cols-[28px_minmax(0,1fr)] gap-x-2 sm:grid-cols-[32px_minmax(0,1fr)] sm:gap-x-3">
      {/* Timeline rail — точка статуса + соединительная линия. */}
      <div className="relative flex flex-col items-center">
        {/* Верхний сегмент линии (скрыт на первом итеме). */}
        <span
          aria-hidden
          className={cn("w-px flex-none bg-border/70", isFirst ? "h-3 bg-transparent" : "h-3")}
        />
        {/* Маркер статуса. */}
        <span
          aria-hidden
          className={cn(
            "z-10 flex size-4 shrink-0 items-center justify-center rounded-full border transition-colors",
            isViewed
              ? "border-green/60 bg-green/85 text-background shadow-[0_0_0_3px_rgba(0,0,0,0.04)]"
              : isLocked
                ? "border-border bg-muted/40 text-muted-foreground"
                : "border-border bg-background",
          )}
        >
          {isViewed ? (
            <Icons.completed className="size-2.5" strokeWidth={3} />
          ) : isLocked ? (
            <Icons.locked className="size-2" strokeWidth={2.5} />
          ) : null}
        </span>
        {/* Нижний сегмент линии (скрыт на последнем итеме). */}
        <span aria-hidden className={cn("w-px flex-1 bg-border/70", isLast && "bg-transparent")} />
      </div>

      {/* Контент */}
      <div className="min-w-0 py-2.5 sm:py-3">
        <div
          className={cn(
            "group/item -mx-2 flex items-start gap-3 rounded-lg px-2 py-1 transition-colors",
            isLocked
              ? "hover:bg-amber-500/[0.04]"
              : "hover:bg-accent/40 focus-visible:bg-accent/40",
          )}
        >
          <RowLink
            isLocked={isLocked}
            href={href}
            lockReason={item.lockReason}
            courseTitle={courseTitle}
            unlockHref={unlockHref}
            secondaryHref={secondaryHref}
          >
            {/* Thumbnail — 16:9, всегда одинакового размера; fallback с kind-icon когда нет превью. */}
            <div className="shrink-0">
              {thumbnailUrl ? (
                <div className="relative aspect-video w-20 sm:w-28 overflow-hidden rounded-md bg-muted">
                  <ContentImage
                    src={thumbnailUrl}
                    alt=""
                    sizes="(min-width: 640px) 7rem, 5rem"
                    className={cn(
                      // rounded-md matches the wrapper: a filter:blur child escapes the
                      // parent's rounded overflow-hidden clip (square corners), so self-clip it.
                      "size-full rounded-md object-cover transition-transform duration-300",
                      !isLocked && "group-hover/item:scale-[1.04]",
                      isLocked && "blur-[1px] opacity-70",
                    )}
                  />
                  {isVideo && !isLocked && (
                    <div className="pointer-events-none absolute inset-0 flex items-center justify-center">
                      <div className="flex size-6 items-center justify-center rounded-full bg-black/55 backdrop-blur-sm">
                        <Icons.play className="ml-0.5 size-2.5 fill-white text-white" />
                      </div>
                    </div>
                  )}
                  {isLocked && (
                    <div className="absolute inset-0 flex items-center justify-center bg-black/40">
                      <Icons.locked className="size-3.5 text-white/90" />
                    </div>
                  )}
                </div>
              ) : (
                <div
                  className={cn(
                    "relative flex aspect-video w-20 sm:w-28 items-center justify-center overflow-hidden rounded-md",
                    kindBadge.iconBgClassName,
                    isLocked && "opacity-70",
                  )}
                  aria-hidden
                >
                  <KindIcon className="size-5 opacity-80" />
                  {isLocked && (
                    <div className="absolute inset-0 flex items-center justify-center bg-background/40">
                      <Icons.locked className="size-3.5 text-foreground/80" />
                    </div>
                  )}
                </div>
              )}
            </div>

            {/* Текстовая колонка */}
            <div className="min-w-0 flex-1">
              {/* Top row: kind badge + viewed pill */}
              <div className="flex flex-wrap items-center gap-x-2 gap-y-1">
                <span
                  className={cn(
                    "inline-flex items-center gap-1 rounded-md px-1.5 py-0.5 text-[10.5px] font-medium border",
                    kindBadge.className,
                  )}
                >
                  <KindIcon className="size-3" />
                  {kindBadge.label}
                </span>
                {isViewed && (
                  <span className="inline-flex items-center gap-1 rounded-md border border-green/30 bg-green/10 px-1.5 py-0.5 text-[10.5px] font-medium text-green">
                    <Icons.completed className="size-3" />
                    Изучено {formatNumericDate(viewedAt!)}
                  </span>
                )}
                {isLocked && lockCopy && (
                  <span className="inline-flex items-center gap-1 text-[10.5px] font-medium text-amber-300/90">
                    <Icons.locked className="size-3" />
                    {lockCopy.shortHint}
                  </span>
                )}
              </div>

              {/* Title with numeric prefix */}
              <p
                className={cn(
                  "mt-1 text-sm leading-snug sm:text-[0.9375rem]",
                  isViewed
                    ? "text-muted-foreground line-through decoration-muted-foreground/40"
                    : isLocked
                      ? "text-muted-foreground"
                      : "text-foreground group-hover/item:text-foreground",
                )}
              >
                <span
                  className={cn(
                    "mr-1 font-semibold tabular-nums",
                    isViewed ? "text-muted-foreground/60 line-through" : "text-muted-foreground",
                  )}
                >
                  {prefix}:
                </span>
                <span className="font-medium">{material.title}</span>
              </p>

              {/* Bottom meta — дата публикации */}
              {publishedDate && (
                <div className="mt-1 text-[10.5px] text-muted-foreground/80">
                  <time dateTime={publishedDate} className="tabular-nums">
                    {formatRelativeDate(publishedDate)}
                  </time>
                </div>
              )}
            </div>
          </RowLink>

          {/* Bookmark — отдельный focusable target вне Link'а, кликается без перехода. */}
          {renderBookmark && courseId && (
            <span
              className="mt-0.5 shrink-0"
              onClick={(e) => e.stopPropagation()}
              onKeyDown={(e) => e.stopPropagation()}
              role="presentation"
            >
              {renderBookmark({
                courseId,
                materialId: item.referenceId,
                className: "size-7",
              })}
            </span>
          )}
        </div>
      </div>
    </li>
  );
}

/**
 * Timeline-итем квиза (#491): тумба с quiz-иконкой, бейдж «Квиз», заголовок и
 * число вопросов. Доступный — ссылка на студенческую страницу standalone-квиза
 * `/quizzes/[quizId]` (ST-16 #495); замок рендерим тем же hint-бейджем, что у
 * материалов (некликабельный).
 */
function CollectionQuizTimelineRow({
  item,
  prefix,
  isFirst,
  isLast,
  courseTitle,
}: {
  item: CollectionItemDto;
  prefix: string;
  isFirst: boolean;
  isLast: boolean;
  courseTitle: string | null;
}) {
  const isLocked = !item.isAccessible;
  const lockCopy = isLocked ? resolveLockCopy(item.lockReason, courseTitle) : null;

  const rowContent = (
    <>
      {/* Thumbnail-блок — quiz-иконка на violet-подложке. */}
      <div
        className={cn(
          "relative flex aspect-video w-20 shrink-0 items-center justify-center overflow-hidden rounded-md bg-violet-500/15 sm:w-28",
          isLocked && "opacity-70",
        )}
        aria-hidden
      >
        <Icons.quiz className="size-5 text-violet-500 opacity-80" />
        {isLocked && (
          <div className="absolute inset-0 flex items-center justify-center bg-background/40">
            <Icons.locked className="size-3.5 text-foreground/80" />
          </div>
        )}
      </div>

      {/* Текстовая колонка */}
      <div className="min-w-0 flex-1">
        <div className="flex flex-wrap items-center gap-x-2 gap-y-1">
          <span className="inline-flex items-center gap-1 rounded-md border border-violet-500/30 bg-violet-500/10 px-1.5 py-0.5 text-[10.5px] font-medium text-violet-400">
            <Icons.quiz className="size-3" />
            Тест
          </span>
          {isLocked && lockCopy && (
            <span className="inline-flex items-center gap-1 text-[10.5px] font-medium text-amber-300/90">
              <Icons.locked className="size-3" />
              {lockCopy.shortHint}
            </span>
          )}
        </div>

        <p
          className={cn(
            "mt-1 text-sm leading-snug sm:text-[0.9375rem]",
            isLocked ? "text-muted-foreground" : "text-foreground",
          )}
        >
          <span className="mr-1 font-semibold tabular-nums text-muted-foreground">{prefix}:</span>
          <span className="font-medium">{item.quizTitle ?? "Тест"}</span>
        </p>

        {item.questionsCount != null && (
          <div className="mt-1 text-[10.5px] tabular-nums text-muted-foreground/80">
            {item.questionsCount} вопр.
          </div>
        )}
      </div>
    </>
  );

  return (
    <li className="relative grid grid-cols-[28px_minmax(0,1fr)] gap-x-2 sm:grid-cols-[32px_minmax(0,1fr)] sm:gap-x-3">
      {/* Timeline rail — как у материала, без viewed-статуса. */}
      <div className="relative flex flex-col items-center">
        <span
          aria-hidden
          className={cn("w-px flex-none bg-border/70", isFirst ? "h-3 bg-transparent" : "h-3")}
        />
        <span
          aria-hidden
          className={cn(
            "z-10 flex size-4 shrink-0 items-center justify-center rounded-full border",
            isLocked
              ? "border-border bg-muted/40 text-muted-foreground"
              : "border-border bg-background",
          )}
        >
          {isLocked ? <Icons.locked className="size-2" strokeWidth={2.5} /> : null}
        </span>
        <span aria-hidden className={cn("w-px flex-1 bg-border/70", isLast && "bg-transparent")} />
      </div>

      {/* Контент */}
      <div className="min-w-0 py-2.5 sm:py-3">
        {isLocked ? (
          <div className="group/item -mx-2 flex items-start gap-3 rounded-lg px-2 py-1">
            {rowContent}
          </div>
        ) : (
          <Link
            href={routes.quiz(item.referenceId)}
            className="group/item -mx-2 flex items-start gap-3 rounded-lg px-2 py-1 transition-colors hover:bg-accent/40 focus-visible:bg-accent/40"
          >
            {rowContent}
          </Link>
        )}
      </div>
    </li>
  );
}

/**
 * Плашка под header'ом гейтнутой подборки. Объясняет почему «целиком» подборка
 * не открыта (lockReason) и подсказывает как разблокировать. Сами материалы
 * внутри подборки остаются видимы — у каждого свой замок/CTA.
 */
function CollectionHeaderLockNotice({
  lockReason,
  courseTitle,
}: {
  lockReason: CollectionLockReason;
  courseTitle: string | null;
}) {
  const copy = resolveLockCopy(lockReason, courseTitle);
  return (
    <div className="flex items-start gap-3 rounded-xl border border-amber-500/30 bg-amber-500/5 p-4 text-amber-200">
      <Icons.locked className="mt-0.5 size-5 shrink-0" />
      <div className="space-y-1">
        <p className="text-sm font-semibold">{copy.title}</p>
        <p className="text-sm text-amber-200/80">{copy.subtitle}</p>
      </div>
    </div>
  );
}
