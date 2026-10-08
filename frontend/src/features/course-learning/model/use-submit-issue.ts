import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { getErrorMessage } from "@/shared/api";
import { courseProgressApi, courseProgressQueryOptions } from "@/entities/course-progress";
import { trackGrowthEvent } from "@/shared/analytics";

export function useSubmitIssue(courseId: string, issueId: string) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (params: { submissionUrl?: string; contentPayload?: string }) =>
      courseProgressApi.submitIssue({
        courseId,
        issueId,
        submissionUrl: params.submissionUrl,
        contentPayload: params.contentPayload,
      }),
    onSuccess: async () => {
      toast.success("Решение отправлено");
      trackGrowthEvent(
        {
          name: "first_issue_submitted",
          properties: { issue_id: issueId, course_id: courseId },
        },
        { once: "activation:first-issue-submitted" },
      );
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
      toast.error(getErrorMessage(error, "Не удалось отправить решение"));
    },
  });
}
