import {
  courseStudentsApi,
  studentProgressQueryOptions,
} from "@/entities/course-student";
import { getErrorMessage } from "@/shared/api";
import type { IssueProgressStatus } from "@/shared/types/status";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

/**
 * #518 — staff-override: выставить ЛЮБОЙ статус прогресса задачи студенту. COMPLETED начисляет XP +
 * каскад project/module; уход из COMPLETED откатывает XP. На success инвалидируем прогресс студента,
 * чтобы панель перерисовала бейдж. Pending трекается по issueId (variables.issueId).
 */
export function useSetIssueStatusForUser(courseId: string, userId: string) {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: (params: {
      issueId: string;
      targetStatus: IssueProgressStatus;
    }) =>
      courseStudentsApi.setIssueStatusForUser({
        courseId,
        userId,
        issueId: params.issueId,
        targetStatus: params.targetStatus,
      }),
    onSuccess: async () => {
      toast.success("Статус задания изменён");
      await queryClient.invalidateQueries({
        queryKey: studentProgressQueryOptions(courseId, userId).queryKey,
      });
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Не удалось изменить статус задания"));
    },
  });

  return {
    setIssueStatusForUser: mutation.mutateAsync,
    pendingIssueId: mutation.isPending ? mutation.variables.issueId : null,
  };
}
