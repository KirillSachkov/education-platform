import { apiClient, type Envelope, nextPageParamFromPagination } from "@/shared/api";
import type { PaginationResponse } from "@/shared/api/pagination-response";
import type { IssueProgressStatus } from "@/shared/types/status";
import { infiniteQueryOptions, keepPreviousData, queryOptions } from "@tanstack/react-query";
import type { CourseStudentDto, GetCourseStudentsRequest, StudentCourseProgressDto } from "./types";

export const courseStudentsApi = {
  getCourseStudents: async (
    courseId: string,
    request: GetCourseStudentsRequest,
    { signal }: { signal?: AbortSignal } = {},
  ) => {
    const res = await apiClient.get<Envelope<PaginationResponse<CourseStudentDto>>>(
      `/progress/courses/${courseId}/students/`,
      { params: request, signal },
    );
    return res.data;
  },

  getStudentProgress: async (
    courseId: string,
    userId: string,
    { signal }: { signal?: AbortSignal } = {},
  ) => {
    const res = await apiClient.get<Envelope<StudentCourseProgressDto>>(
      `/progress/courses/${courseId}/students/${userId}/progress/`,
      { signal },
    );
    return res.data;
  },

  /**
   * Staff-override (#518): выставить ЛЮБОЙ статус прогресса задачи студенту из полной палитры
   * (NOT_STARTED / IN_PROGRESS / UNDER_REVIEW / REQUESTED_CHANGES / COMPLETED). COMPLETED начисляет
   * XP + каскад project/module; уход из COMPLETED откатывает XP. UNDER_REVIEW / REQUESTED_CHANGES
   * только двигают бейдж — не кладут работу в очередь проверки.
   */
  setIssueStatusForUser: async (params: {
    courseId: string;
    issueId: string;
    userId: string;
    targetStatus: IssueProgressStatus;
  }) => {
    const res = await apiClient.put<Envelope<void>>(
      `/progress/courses/${params.courseId}/issues/${params.issueId}/progress-status-for-user/`,
      { userId: params.userId, targetStatus: params.targetStatus },
    );
    return res.data;
  },

  /** Staff-override (#398): отметить материал изученным за студента. */
  markMaterialViewedForUser: async (params: { materialId: string; userId: string }) => {
    const res = await apiClient.post<Envelope<void>>(
      `/progress/materials/${params.materialId}/view-for-user/`,
      { userId: params.userId },
    );
    return res.data;
  },
};

export const courseStudentsQueryOptions = {
  baseKey: "course-students",
};

export const courseStudentsInfiniteQueryOptions = (
  courseId: string,
  request: { pageSize: number; search?: string },
) =>
  infiniteQueryOptions({
    queryKey: [courseStudentsQueryOptions.baseKey, courseId, request],
    queryFn: ({ pageParam, signal }) =>
      courseStudentsApi.getCourseStudents(
        courseId,
        {
          page: pageParam,
          pageSize: request.pageSize,
          search: request.search,
        },
        { signal },
      ),
    initialPageParam: 1,
    getNextPageParam: (lastPage) => nextPageParamFromPagination(lastPage),
    select: (data) => ({
      items: data.pages.flatMap((page) => page.result?.items ?? []),
      totalCount: data.pages[0]?.result?.totalCount ?? 0,
      totalPages: data.pages[0]?.result?.totalPages ?? 0,
    }),
    placeholderData: keepPreviousData,
    enabled: !!courseId,
  });

export const studentProgressQueryOptions = (courseId: string, userId: string) =>
  queryOptions({
    queryKey: [courseStudentsQueryOptions.baseKey, courseId, userId, "progress"],
    queryFn: ({ signal }) => courseStudentsApi.getStudentProgress(courseId, userId, { signal }),
    select: (data) => data.result!,
    enabled: !!courseId && !!userId,
  });
