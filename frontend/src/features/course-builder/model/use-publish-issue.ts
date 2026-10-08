import { invalidateEducationContent } from "@/entities/course";
import { issuesApi } from "@/entities/issue";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

export interface PublishIssueInput {
  issueId: string;
  notifySubscribers: boolean;
}

export function usePublishIssue(_projectId: string) {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: ({ issueId, notifySubscribers }: PublishIssueInput) =>
      issuesApi.publishIssue(issueId, { notifySubscribers }),
    onSuccess: async () => {
      toast.success("Задача опубликована");
      await invalidateEducationContent(queryClient);
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка публикации задачи"));
    },
  });

  return {
    publishIssue: mutation.mutateAsync,
    isPending: mutation.isPending,
  };
}
