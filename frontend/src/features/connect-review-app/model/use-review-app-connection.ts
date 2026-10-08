import { useQuery } from "@tanstack/react-query";
import { myInstallationsQueryOptions } from "@/entities/vcs-installation";
import { unwrapEnvelope } from "@/shared/api";

/**
 * Есть ли у текущего пользователя активная установка review-app (ARS GitHub App).
 * Используется гейтом сдачи PR (`ReviewConnectionGate`) и студенческой карточкой
 * в настройках. `enabled` гасит запрос там, где проверка не нужна (не на
 * submit-экране задачи / гость) — список установок не тянется зря.
 */
export function useReviewAppConnection(options?: { enabled?: boolean }) {
  const enabled = options?.enabled ?? true;

  const { data, isPending, isError } = useQuery({
    ...myInstallationsQueryOptions(),
    enabled,
  });

  const hasActiveInstallation =
    !!data && unwrapEnvelope(data).installations.some((i) => i.status === "ACTIVE");

  return {
    hasActiveInstallation,
    // Выключенный query остаётся в состоянии pending — для caller'а это «не грузим».
    isLoading: enabled && isPending,
    isError,
  };
}
