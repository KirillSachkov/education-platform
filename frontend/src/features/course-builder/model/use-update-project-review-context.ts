import { invalidateEducationContent } from "@/entities/course";
import { projectsApi, projectsQueryOptions } from "@/entities/project";
import type { UpdateProjectReviewContextRequest } from "@/entities/project";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

export function useUpdateProjectReviewContext(projectId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (request: UpdateProjectReviewContextRequest) =>
      projectsApi.updateReviewContext({ projectId, request }),
    onSuccess: async () => {
      toast.success("Guidelines AI-проверки сохранены");
      await Promise.all([
        queryClient.invalidateQueries({
          queryKey: [projectsQueryOptions.baseKey, projectId, "review-context"],
        }),
        queryClient.invalidateQueries({
          queryKey: [projectsQueryOptions.baseKey, projectId, "detail"],
        }),
        invalidateEducationContent(queryClient),
      ]);
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка сохранения guidelines"));
    },
  });
}
