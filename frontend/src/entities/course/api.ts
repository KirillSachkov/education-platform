import { apiClient, type Envelope } from "@/shared/api";
import type { CourseKind } from "@/shared/config/course-kind";
import type { CursorResponse } from "@/shared/api/cursor-response";
import { infiniteQueryOptions, queryOptions } from "@tanstack/react-query";
import type {
  CourseBuilderDto,
  CourseCatalogDto,
  CourseCurriculumDto,
  CourseDetailDto,
  CourseId,
  CourseLandingDto,
  CourseSummaryDto,
  CreateCourseModuleRequest,
  CreateCourseProjectRequest,
  CreateCourseRequest,
  GetCatalogRequest,
  GetMyCoursesRequest,
  MoveCourseItemRequest,
  MoveCourseRequest,
  PendingCatalogCourseDto,
  PlatformContentStatsDto,
  ReassignCourseAuthorRequest,
  ReassignCourseAuthorResponse,
  UpdateCourseRequest,
} from "./types";

export const coursesApi = {
  getMyCourses: async (request: GetMyCoursesRequest, { signal }: { signal: AbortSignal }) => {
    const res = await apiClient.get<Envelope<CursorResponse<CourseSummaryDto>>>("/courses/my/", {
      params: request,
      signal,
    });
    return res.data;
  },

  createCourse: async (request: CreateCourseRequest) => {
    const res = await apiClient.post<Envelope<string>>("/courses/", request);
    return res.data;
  },

  updateCourse: async ({
    courseId,
    request,
  }: {
    courseId: CourseId;
    request: UpdateCourseRequest;
  }) => {
    const res = await apiClient.patch<Envelope<string>>(`/courses/${courseId}`, request);
    return res.data;
  },
  deleteCourse: async (courseId: CourseId) => {
    const res = await apiClient.delete<Envelope<string>>(`/courses/${courseId}`);
    return res.data;
  },

  reassignCourseAuthor: async ({ courseId, newAuthorId }: ReassignCourseAuthorRequest) => {
    const res = await apiClient.patch<Envelope<ReassignCourseAuthorResponse>>(
      `/courses/${courseId}/author/`,
      { newAuthorId },
    );
    return res.data;
  },

  publishCourse: async (courseId: CourseId) => {
    const res = await apiClient.post<Envelope<string>>(`/courses/${courseId}/publish`);
    return res.data;
  },

  archiveCourse: async (courseId: CourseId) => {
    const res = await apiClient.post<Envelope<string>>(`/courses/${courseId}/archive`);
    return res.data;
  },

  restoreCourse: async (courseId: CourseId) => {
    const res = await apiClient.post<Envelope<string>>(`/courses/${courseId}/restore`);
    return res.data;
  },

  toggleIsNew: async ({ courseId, isNew }: { courseId: CourseId; isNew: boolean }) => {
    const res = await apiClient.patch<Envelope<string>>(`/courses/${courseId}/is-new/`, { isNew });
    return res.data;
  },

  getCourseDetail: async (courseId: CourseId, { signal }: { signal?: AbortSignal } = {}) => {
    const res = await apiClient.get<Envelope<CourseDetailDto>>(`/courses/${courseId}/detail`, {
      signal,
    });
    return res.data;
  },

  createModule: async ({
    courseId,
    request,
  }: {
    courseId: CourseId;
    request: CreateCourseModuleRequest;
  }) => {
    const res = await apiClient.post<Envelope<string>>(`/courses/${courseId}/modules/`, request);
    return res.data;
  },

  createProject: async ({
    courseId,
    request,
  }: {
    courseId: CourseId;
    request: CreateCourseProjectRequest;
  }) => {
    const res = await apiClient.post<Envelope<string>>(`/courses/${courseId}/projects/`, request);
    return res.data;
  },

  moveCourseItem: async ({
    courseId,
    referenceId,
    request,
  }: {
    courseId: CourseId;
    referenceId: string;
    request: MoveCourseItemRequest;
  }) => {
    const res = await apiClient.patch<Envelope<string>>(
      `/courses/${courseId}/items/${referenceId}/move`,
      request,
    );
    return res.data;
  },

  moveCourse: async ({ courseId, request }: { courseId: CourseId; request: MoveCourseRequest }) => {
    const res = await apiClient.patch<Envelope<string>>(`/courses/${courseId}/move`, request);
    return res.data;
  },

  detachCourseItem: async ({
    courseId,
    referenceId,
  }: {
    courseId: CourseId;
    referenceId: string;
  }) => {
    const res = await apiClient.delete<Envelope<string>>(
      `/courses/${courseId}/items/${referenceId}`,
    );
    return res.data;
  },

  getCatalog: async (request: GetCatalogRequest, { signal }: { signal?: AbortSignal } = {}) => {
    const res = await apiClient.get<Envelope<CursorResponse<CourseCatalogDto>>>(
      "/courses/catalog/",
      { params: request, signal },
    );
    return res.data;
  },

  getCurriculum: async (courseId: CourseId, { signal }: { signal?: AbortSignal } = {}) => {
    const res = await apiClient.get<Envelope<CourseCurriculumDto>>(
      `/courses/${courseId}/curriculum/`,
      { signal },
    );
    const curriculum = res.data.result;
    if (!curriculum) throw new Error("Course curriculum response has no result");
    return curriculum;
  },

  getCourseBuilder: async (courseId: CourseId, { signal }: { signal?: AbortSignal } = {}) => {
    const res = await apiClient.get<Envelope<CourseBuilderDto>>(`/courses/${courseId}/builder`, {
      signal,
    });
    return res.data;
  },

  getCourseLanding: async (courseId: CourseId, { signal }: { signal?: AbortSignal } = {}) => {
    const res = await apiClient.get<Envelope<CourseLandingDto>>(`/courses/${courseId}/landing`, {
      signal,
    });
    return res.data;
  },

  resolveBySlug: async (slug: string, { signal }: { signal?: AbortSignal } = {}) => {
    const res = await apiClient.get<Envelope<{ courseId: string; slug: string }>>(
      `/courses/by-slug/${slug}/`,
      { signal },
    );
    const course = res.data.result;
    if (!course) throw new Error("Course slug response has no result");
    return course;
  },

  getByAuthor: async (
    authorId: string,
    params?: { cursor?: string; limit?: number; kind?: string },
    { signal }: { signal?: AbortSignal } = {},
  ) => {
    const res = await apiClient.get<Envelope<CursorResponse<CourseCatalogDto>>>(
      `/courses/by-author/${authorId}/`,
      { params, signal },
    );
    return res.data;
  },

  getPlatformContentStats: async ({ signal }: { signal?: AbortSignal } = {}) => {
    const res = await apiClient.get<Envelope<PlatformContentStatsDto>>("/courses/platform-stats/", {
      signal,
    });
    return res.data;
  },

  /**
   * Очередь модерации витрины (#569): PUBLISHED-курсы, ожидающие одобрения к показу
   * в каталоге (`isCatalogListed=false`). Permission `content.moderate`.
   */
  getPendingListing: async (
    params: { cursor?: string; limit?: number },
    { signal }: { signal?: AbortSignal } = {},
  ) => {
    const res = await apiClient.get<Envelope<CursorResponse<PendingCatalogCourseDto>>>(
      "/courses/admin/pending-listing/",
      { params, signal },
    );
    return res.data;
  },

  /**
   * Одобрить/снять одобрение показа курса в каталоге (#569). Permission `content.moderate`.
   */
  setCatalogListing: async ({ courseId, listed }: { courseId: CourseId; listed: boolean }) => {
    const res = await apiClient.patch<Envelope<string>>(`/courses/${courseId}/catalog-listing/`, {
      listed,
    });
    return res.data;
  },
};

export const courseDetailQueryOptions = (courseId: CourseId) =>
  queryOptions({
    queryKey: [coursesQueryOptions.baseKey, courseId, "detail"],
    queryFn: ({ signal }) => coursesApi.getCourseDetail(courseId, { signal }),
    select: (data) => data.result!,
    staleTime: 5 * 60 * 1000,
  });

export const catalogQueryOptions = ({
  limit = 12,
  search,
  kind,
}: {
  limit?: number;
  search?: string;
  kind?: CourseKind | undefined;
}) =>
  infiniteQueryOptions({
    queryKey: [coursesQueryOptions.baseKey, "catalog", { limit, search, kind }],
    queryFn: ({ pageParam, signal }) =>
      coursesApi.getCatalog({ limit, cursor: pageParam, search, kind }, { signal }),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (lastPage) => lastPage.result?.nextCursor ?? undefined,
    select: (data) => ({
      items: data.pages.flatMap((page) => page.result?.items ?? []),
      totalCount: data.pages[0]?.result?.totalCount ?? 0,
    }),
  });

/**
 * Агрегированные счётчики опубликованного контента платформы (курсы/материалы/
 * задачи/подборки) для блока статистики на карточке «Полный доступ» (/pricing).
 * Меняются редко — staleTime 10 мин, бэкенд тоже кеширует.
 */
export const platformContentStatsQueryOptions = () =>
  queryOptions({
    queryKey: [coursesQueryOptions.baseKey, "platform-stats"],
    queryFn: ({ signal }) => coursesApi.getPlatformContentStats({ signal }),
    select: (data) => data.result!,
    staleTime: 10 * 60 * 1000,
  });

/**
 * Лёгкий derived-флаг «идёт ли акция хоть на один курс/интенсив» — для индикатора
 * в сайтбаре. Тянет первую страницу каталога (курсов мало) и схлопывает в
 * `{ active, maxDiscount }`. Отдельный queryKey + staleTime 5min, чтобы сайдбар,
 * примонтированный в layout, не дёргал запрос на каждой навигации.
 */
export const activePromotionQueryOptions = () =>
  queryOptions({
    queryKey: [coursesQueryOptions.baseKey, "catalog", "active-promotion"],
    queryFn: ({ signal }) => coursesApi.getCatalog({ limit: 100 }, { signal }),
    select: (data) => {
      let active = false;
      let maxDiscount = 0;
      for (const course of data.result?.items ?? []) {
        const pricing = course.pricing;
        if (pricing?.promotionActive) {
          active = true;
          if (pricing.discountPercent && pricing.discountPercent > maxDiscount) {
            maxDiscount = pricing.discountPercent;
          }
        }
      }
      return { active, maxDiscount };
    },
    staleTime: 5 * 60 * 1000,
  });

export const courseCurriculumQueryOptions = (courseId: CourseId) =>
  queryOptions({
    queryKey: [coursesQueryOptions.baseKey, courseId, "curriculum"],
    queryFn: ({ signal }) => coursesApi.getCurriculum(courseId, { signal }),
    // Curriculum changes only when an author edits the course. Backend uses
    // HybridCache (3min distributed + 30s L1) — match the L1 window so the
    // sidebar mounted in layout doesn't refetch on every page navigation.
    staleTime: 5 * 60 * 1000,
    enabled: !!courseId,
  });

export const courseBuilderQueryOptions = (courseId: CourseId) =>
  queryOptions({
    queryKey: [coursesQueryOptions.baseKey, courseId, "builder"],
    queryFn: ({ signal }) => coursesApi.getCourseBuilder(courseId, { signal }),
    select: (data) => data.result!,
    staleTime: 60_000,
    enabled: !!courseId,
  });

export const courseLandingQueryOptions = (courseId: CourseId) =>
  queryOptions({
    queryKey: [coursesQueryOptions.baseKey, courseId, "landing"],
    queryFn: ({ signal }) => coursesApi.getCourseLanding(courseId, { signal }),
    select: (data) => data.result!,
    staleTime: 5 * 60 * 1000,
    enabled: !!courseId,
  });

export const courseSlugResolveQueryOptions = (slug: string) =>
  queryOptions({
    queryKey: [coursesQueryOptions.baseKey, "by-slug", slug],
    queryFn: ({ signal }) => coursesApi.resolveBySlug(slug, { signal }),
    staleTime: 5 * 60 * 1000,
    enabled: !!slug,
  });

export const authorCoursesQueryOptions = (authorId: string, kind?: CourseKind) =>
  queryOptions({
    queryKey: [coursesQueryOptions.baseKey, "by-author", authorId, { kind }],
    // `limit` обязателен — бэкенд валидирует InclusiveBetween(1,100), без него 400.
    queryFn: ({ signal }) => coursesApi.getByAuthor(authorId, { limit: 24, kind }, { signal }),
    select: (data) => data.result!,
    enabled: !!authorId,
    staleTime: 5 * 60 * 1000,
  });

/**
 * Очередь модерации витрины (#569) — PUBLISHED-курсы, ждущие одобрения к показу
 * в каталоге. Cursor-инфинит, permission `content.moderate`.
 */
export const pendingCatalogListingInfiniteOptions = ({ limit = 20 }: { limit?: number } = {}) =>
  infiniteQueryOptions({
    queryKey: [coursesQueryOptions.baseKey, "pending-listing", { limit }],
    queryFn: ({ pageParam, signal }) =>
      coursesApi.getPendingListing({ limit, cursor: pageParam }, { signal }),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (lastPage) => lastPage.result?.nextCursor ?? undefined,
    select: (data) => ({
      items: data.pages.flatMap((page) => page.result?.items ?? []),
      totalCount: data.pages[0]?.result?.totalCount ?? 0,
    }),
  });

export const coursesQueryOptions = {
  baseKey: "courses",

  getMyCoursesInfiniteOptions: (filter: { limit: number; kind?: CourseKind }) => {
    return infiniteQueryOptions({
      queryKey: [coursesQueryOptions.baseKey, "my", filter],
      queryFn: ({ pageParam, signal }) => {
        const request: GetMyCoursesRequest = {
          ...filter,
          cursor: pageParam,
        };
        return coursesApi.getMyCourses(request, { signal });
      },
      initialPageParam: undefined as string | undefined,
      getNextPageParam: (lastPage) => {
        const result = lastPage.result;
        if (!result) return undefined;
        return result.nextCursor ? result.nextCursor : undefined;
      },
      select: (data) => ({
        items: data.pages.flatMap((page) => page.result?.items ?? []),
        totalCount: data.pages[0]?.result?.totalCount ?? 0,
      }),
    });
  },
};
