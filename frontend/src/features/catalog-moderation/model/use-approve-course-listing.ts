import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { coursesApi, coursesQueryOptions } from "@/entities/course";
import { getErrorMessage } from "@/shared/api";

/**
 * Одобрить (или снять одобрение) показ курса в публичном каталоге (#569).
 * После успеха инвалидирует очередь модерации + каталожные запросы, чтобы
 * одобренный курс ушёл из очереди и появился в каталоге.
 */
export function useApproveCourseListing() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ courseId, listed }: { courseId: string; listed: boolean }) =>
      coursesApi.setCatalogListing({ courseId, listed }),
    onSuccess: async (_data, variables) => {
      toast.success(variables.listed ? "Курс одобрен" : "Курс скрыт из каталога");
      // baseKey "courses" покрывает pending-listing, catalog, by-author, my — снести разом.
      await queryClient.invalidateQueries({ queryKey: [coursesQueryOptions.baseKey] });
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка модерации курса"));
    },
  });
}
