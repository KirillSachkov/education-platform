import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { getErrorMessage } from "@/shared/api";
import { courseProgressApi, courseProgressQueryOptions } from "@/entities/course-progress";

export function useStartIssue(courseId: string, issueId: string) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (params: { projectId: string }) =>
      courseProgressApi.startIssueWork({
        courseId,
        projectId: params.projectId,
        issueId,
      }),
    onSuccess: async () => {
      toast.success("Работа над задачей начата");
      await Promise.all([
        queryClient.invalidateQueries({
          queryKey: [courseProgressQueryOptions.baseKey, courseId],
        }),
        queryClient.invalidateQueries({
          queryKey: [courseProgressQueryOptions.issueHistoryKey, courseId, issueId],
        }),
      ]);
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Не удалось начать работу над задачей"));
    },
  });
}
