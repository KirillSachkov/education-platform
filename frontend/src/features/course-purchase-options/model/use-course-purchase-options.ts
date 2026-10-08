"use client";

import { useQuery } from "@tanstack/react-query";
import { planCoversCourse, publicPlansQueryOptions } from "@/entities/access-plan";

/**
 * Планы платформы, которые покрывают конкретный курс (#404/#608). Фильтрация
 * client-side из общего `publicPlansQueryOptions()` — react-query
 * дедупит запрос с pricing-страницей и каталогом.
 */
export function useCoursePurchaseOptions(courseId: string) {
  const query = useQuery({
    ...publicPlansQueryOptions(),
    enabled: Boolean(courseId),
  });

  const coveringPlans = (query.data ?? []).filter((plan) => planCoversCourse(plan, courseId));

  return {
    coveringPlans,
    isLoading: query.isLoading,
  };
}
