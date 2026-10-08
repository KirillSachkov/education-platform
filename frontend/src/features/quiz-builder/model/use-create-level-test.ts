import { levelTestQueryOptions } from "@/entities/level-test";
import { quizQueryOptions, quizzesApi } from "@/entities/quiz";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { buildLevelTestCreateRequest } from "./level-test-schemas";

/**
 * CTA «Создать тест уровня»: POST DRAFT level-test с дефолтными порогами
 * (JUNIOR 0 / MIDDLE 45 / SENIOR 75) и пустыми секциями. Issue #487.
 */
export function useCreateLevelTest() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: () => quizzesApi.createQuiz(buildLevelTestCreateRequest()),
    onSuccess: async () => {
      toast.success("Тест уровня создан");
      await queryClient.invalidateQueries({ queryKey: quizQueryOptions.myLevelTestsKey() });
    },
    onError: (error) => toast.error(getErrorMessage(error, "Ошибка создания теста уровня")),
  });
}

/** Общая инвалидация редактора level-test'а: авторский список + публичный активный тест. */
export async function invalidateLevelTestEditor(queryClient: ReturnType<typeof useQueryClient>) {
  await Promise.all([
    queryClient.invalidateQueries({ queryKey: quizQueryOptions.myLevelTestsKey() }),
    queryClient.invalidateQueries({ queryKey: levelTestQueryOptions.activeKey() }),
  ]);
}
