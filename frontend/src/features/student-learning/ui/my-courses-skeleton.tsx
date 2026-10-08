"use client";

import { TileCardSkeleton } from "@/shared/ui/components";

interface MyCoursesSkeletonProps {
  count?: number;
}

export function MyCoursesSkeleton({ count = 2 }: MyCoursesSkeletonProps) {
  return <TileCardSkeleton count={count} />;
}
