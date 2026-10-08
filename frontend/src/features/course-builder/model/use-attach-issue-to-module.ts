import { invalidateEducationContent } from "@/entities/course";
import { modulesApi } from "@/entities/module";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

export function useAttachIssueToModule(_courseId: string, moduleId: string) {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: (issueId: string) => modulesApi.attachIssue({ moduleId, issueId }),
    onSuccess: async () => {
      toast.success("Задача добавлена в модуль");
      await invalidateEducationContent(queryClient);
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка добавления задачи"));
    },
  });

  return {
    attachIssue: mutation.mutateAsync,
    isPending: mutation.isPending,
  };
}
