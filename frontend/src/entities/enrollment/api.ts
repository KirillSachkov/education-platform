import { apiClient, type Envelope } from "@/shared/api";
import type { CursorResponse } from "@/shared/api/cursor-response";
import { infiniteQueryOptions, queryOptions } from "@tanstack/react-query";
import type {
  CourseEnrollmentProgressDto,
  GetMyCourseProgressRequest,
  LastActiveCourseDto,
  UserCourseProgressDto,
} from "./types";

export const enrollmentApi = {
  getMyEnrollment: async (courseId: string, { signal }: { signal?: AbortSignal } = {}) => {
    const res = await apiClient.get<Envelope<CourseEnrollmentProgressDto | null>>(
      `/progress/courses/${courseId}/my-enrollment/`,
      { signal },
    );
    return res.data;
  },

  getMyCourseProgress: async (
    request: GetMyCourseProgressRequest,
    { signal }: { signal?: AbortSignal } = {},
  ) => {
    const res = await apiClient.get<Envelope<CursorResponse<UserCourseProgressDto>>>(
      "/progress/courses/my/progress/",
      { params: request, signal },
    );
    return res.data;
  },

  getLastActiveCourse: async ({ signal }: { signal?: AbortSignal } = {}) => {
    const res = await apiClient.get<Envelope<LastActiveCourseDto | null>>(
      "/progress/courses/my/last-active/",
      { signal },
    );
    return res.data;
  },
};

export const myEnrollmentQueryOptions = (courseId: string) =>
  queryOptions({
    queryKey: [enrollmentQueryOptions.baseKey, courseId, "my"],
    queryFn: ({ signal }) => enrollmentApi.getMyEnrollment(courseId, { signal }),
    select: (data) => data.result ?? null,
    staleTime: 60_000,
    enabled: !!courseId,
  });

export const lastActiveCourseQueryOptions = () =>
  queryOptions({
    queryKey: [enrollmentQueryOptions.baseKey, "last-active-course"],
    queryFn: ({ signal }) => enrollmentApi.getLastActiveCourse({ signal }),
    select: (data) => data.result ?? null,
    staleTime: 60_000,
  });

export const enrollmentQueryOptions = {
  baseKey: "enrollments",

  getMyCourseProgressInfiniteOptions: ({ limit }: { limit: number }) =>
    infiniteQueryOptions({
      queryKey: [enrollmentQueryOptions.baseKey, "my-course-progress", { limit }],
      queryFn: ({ pageParam, signal }) =>
        enrollmentApi.getMyCourseProgress({ limit, cursor: pageParam }, { signal }),
      initialPageParam: undefined as string | undefined,
      getNextPageParam: (lastPage) => lastPage.result?.nextCursor ?? undefined,
      select: (data) => ({
        items: data.pages.flatMap((page) => page.result?.items ?? []),
        totalCount: data.pages[0]?.result?.totalCount ?? 0,
      }),
    }),

  getMyCourseProgressByAuthorInfiniteOptions: ({
    limit,
    authorId,
  }: {
    limit: number;
    authorId: string | undefined;
  }) =>
    infiniteQueryOptions({
      queryKey: [enrollmentQueryOptions.baseKey, "my-course-progress", { limit, authorId }],
      queryFn: ({ pageParam, signal }) =>
        enrollmentApi.getMyCourseProgress({ limit, cursor: pageParam, authorId }, { signal }),
      initialPageParam: undefined as string | undefined,
      getNextPageParam: (lastPage) => lastPage.result?.nextCursor ?? undefined,
      select: (data) => ({
        items: data.pages.flatMap((page) => page.result?.items ?? []),
        totalCount: data.pages[0]?.result?.totalCount ?? 0,
      }),
      enabled: !!authorId,
    }),
};
