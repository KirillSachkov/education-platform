import { cn } from "@/shared/lib/css";
import { Card, CardContent } from "@/shared/ui/kit/card";
import { Skeleton } from "@/shared/ui/kit/skeleton";

interface TileCardSkeletonProps {
  count?: number;
  className?: string;
  /** Tailwind grid classes applied to the wrapper. */
  gridClassName?: string;
}

/**
 * Grid of card-style skeletons used while loading catalogs or course grids.
 * Mirrors the layout of course/material tile cards: cover + title + 2 lines
 * of description + a progress bar + a bottom action area.
 */
export function TileCardSkeleton({
  count = 3,
  className,
  gridClassName = "grid gap-4 grid-cols-1 md:grid-cols-2 lg:grid-cols-3",
}: TileCardSkeletonProps) {
  return (
    <div className={cn(gridClassName, className)}>
      {Array.from({ length: count }).map((_, index) => (
        <Card key={index} className="overflow-hidden gap-0 py-0">
          <Skeleton className="h-36 rounded-none" />
          <CardContent className="p-4 space-y-3">
            <Skeleton className="h-4 w-2/3" />
            <div className="space-y-2">
              <Skeleton className="h-3 w-full" />
              <Skeleton className="h-3 w-4/5" />
            </div>
            <div className="space-y-2">
              <Skeleton className="h-3 w-full" />
              <Skeleton className="h-2 w-full" />
            </div>
            <Skeleton className="h-9 w-full" />
          </CardContent>
        </Card>
      ))}
    </div>
  );
}

interface RowCardSkeletonProps {
  count?: number;
  className?: string;
}

/**
 * Horizontal row skeleton for list/feed cards (thumbnail + text column).
 * Used by material/article/issue feeds while loading.
 */
export function RowCardSkeleton({ count = 3, className }: RowCardSkeletonProps) {
  return (
    <div className={cn("space-y-3", className)}>
      {Array.from({ length: count }).map((_, index) => (
        <div
          key={index}
          className="rounded-xl border border-border/60 bg-card"
        >
          <div className="flex gap-4 p-3 sm:p-4">
            <Skeleton className="w-36 sm:w-44 aspect-video rounded-lg shrink-0" />
            <div className="flex-1 space-y-2">
              <Skeleton className="h-3 w-36" />
              <Skeleton className="h-4 w-4/5" />
              <Skeleton className="h-3 w-full" />
              <Skeleton className="h-3 w-2/3" />
            </div>
          </div>
        </div>
      ))}
    </div>
  );
}
