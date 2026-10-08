import { apiClient, type Envelope } from "@/shared/api";
import { queryOptions, useQuery } from "@tanstack/react-query";
import type { BillingConfigDto } from "./types";

export const billingConfigApi = {
  getConfig: async ({ signal }: { signal?: AbortSignal } = {}) => {
    const res = await apiClient.get<Envelope<BillingConfigDto>>("/access/billing-config", {
      signal,
    });
    return res.data;
  },
  updateConfig: async (isEnabled: boolean) => {
    const res = await apiClient.patch<Envelope<BillingConfigDto>>("/access/billing-config", {
      isEnabled,
    });
    return res.data;
  },
};

export const billingConfigQueryKey = ["billing-config"] as const;

export const billingConfigQueryOptions = () =>
  queryOptions({
    queryKey: billingConfigQueryKey,
    queryFn: ({ signal }) => billingConfigApi.getConfig({ signal }),
    select: (data) => data.result!,
    // Короткий stale: при экстренном выключении оплаты админом (напр. сбой T-Bank)
    // изменение должно дойти до открытых вкладок быстро. Запрос крошечный (1 bool).
    staleTime: 30 * 1000,
  });

/**
 * Рантайм-флаг приёма оплаты. Пока грузится — false (показываем Telegram-fallback,
 * а не мигаем кнопкой «Оплатить»).
 */
export function useBillingEnabled(): boolean {
  const { data } = useQuery(billingConfigQueryOptions());
  return data?.isEnabled ?? false;
}
