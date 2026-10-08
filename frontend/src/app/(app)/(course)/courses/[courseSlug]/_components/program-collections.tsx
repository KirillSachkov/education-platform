"use client";

import Link from "next/link";
import {
  canAccessItem,
  type CourseAccessLevel,
  type CurriculumCollectionDto,
} from "@/entities/course";
import { CollectionCoverImage, getCollectionGradient } from "@/entities/collection";
import { routes } from "@/shared/config/routes";
import { cn } from "@/shared/lib/css";
import { pluralize } from "@/shared/lib/pluralize";
import { Icons } from "@/shared/ui/icons";

interface ProgramCollectionsProps {
  collections: CurriculumCollectionDto[];
  /** id материалов со статусом VIEWED из learning-state (`is_completed=TRUE`). */
  viewedMaterialIds: Set<string>;
  /** Прогресс «X из Y» показываем только когда learning-state загружен (auth). */
  showProgress: boolean;
  accessLevel: CourseAccessLevel;
  courseSlug: string;
}

/**
 * Блок «Подборки курса» на странице программы (#508) — отдельный чистый список
 * ПОД модулями, типы не смешиваются. Y (itemsCount) считает только
 * PUBLISHED-материалы и согласован со знаменателями прогресс-blueprint'а
 * (#496) — материалы подборок перестают быть «фантомными» в общем счётчике.
 * X считается локально: materialIds ∩ viewed из learning-state, без новых
 * запросов. Замок — client-side по accessType (как у item'ов программы);
 * клик не блокируется — detail подборки partial-access, как в базе знаний.
 */
export function ProgramCollections({
  collections,
  viewedMaterialIds,
  showProgress,
  accessLevel,
  courseSlug,
}: ProgramCollectionsProps) {
  if (collections.length === 0) return null;

  return (
    <section className="mt-8">
      <div className="mb-3">
        <h2 className="text-lg font-semibold tracking-tight text-foreground">Подборки курса</h2>
        <p className="mt-0.5 text-xs text-muted-foreground">
          Дополнительные материалы вне модулей — учитываются в общем прогрессе курса
        </p>
      </div>

      <div className="grid gap-2 sm:grid-cols-2">
        {collections.map((collection) => {
          const total = collection.itemsCount;
          const completed = collection.materialIds.filter((id) =>
            viewedMaterialIds.has(id),
          ).length;
          const isComplete = showProgress && total > 0 && completed >= total;
          const percent = total > 0 ? Math.round((completed / total) * 100) : 0;
          const isLocked = !canAccessItem(collection.accessType, accessLevel);

          return (
            <Link
              key={collection.id}
              href={routes.courseCollectionDetail(courseSlug, collection.id)}
              className="group flex items-center gap-3 rounded-xl border border-border/50 bg-card/30 p-3 transition-all hover:border-border/80 hover:bg-card/40"
            >
              <div className="relative h-14 w-11 shrink-0 overflow-hidden rounded-lg">
                <CollectionCoverImage
                  src={collection.coverUrl}
                  alt=""
                  fill
                  sizes="44px"
                  className="object-cover"
                  fallback={
                    <div
                      className={cn(
                        "absolute inset-0 flex items-center justify-center bg-gradient-to-br",
                        getCollectionGradient(collection.id),
                      )}
                    >
                      <Icons.layers className="size-4 text-white/35" aria-hidden />
                    </div>
                  }
                />
              </div>

              <div className="min-w-0 flex-1">
                <div className="flex items-center gap-1.5">
                  {isLocked && (
                    <Icons.locked size={11} className="shrink-0 text-muted-foreground/60" />
                  )}
                  <h3
                    className={cn(
                      "truncate text-sm font-semibold leading-tight text-foreground",
                      isComplete && "text-muted-foreground",
                    )}
                    title={collection.title}
                  >
                    {collection.title}
                  </h3>
                </div>
                <p className="mt-0.5 text-xs text-muted-foreground tabular-nums">
                  {showProgress ? (
                    <>
                      <span className={cn("font-medium", isComplete ? "text-green" : "text-primary")}>
                        {completed} из {total}
                      </span>{" "}
                      {pluralize(total, "элемента", "элементов", "элементов")} пройдено
                    </>
                  ) : (
                    <>{total} {pluralize(total, "элемент", "элемента", "элементов")}</>
                  )}
                </p>
                {showProgress && total > 0 && (
                  <div
                    className="mt-1.5 h-1 w-full overflow-hidden rounded-full bg-muted"
                    role="progressbar"
                    aria-valuenow={percent}
                    aria-valuemin={0}
                    aria-valuemax={100}
                  >
                    <div
                      className={cn(
                        "h-full rounded-full transition-[width] duration-700",
                        isComplete ? "bg-green" : "bg-primary",
                      )}
                      style={{ width: `${Math.min(100, percent)}%` }}
                    />
                  </div>
                )}
              </div>

              {isComplete ? (
                <span className="inline-flex size-5 shrink-0 items-center justify-center rounded-full border-[1.5px] border-green/70 bg-green/15">
                  <Icons.check size={11} strokeWidth={3} className="text-green" />
                </span>
              ) : (
                <Icons.chevronRight
                  size={16}
                  className="shrink-0 text-muted-foreground/50 transition-transform group-hover:translate-x-0.5"
                />
              )}
            </Link>
          );
        })}
      </div>
    </section>
  );
}
