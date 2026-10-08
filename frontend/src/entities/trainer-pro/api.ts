import { apiClient, type Envelope } from "@/shared/api";
import { queryOptions } from "@tanstack/react-query";
import type {
  CreateTrainerProOfferRequest,
  CreateTrainerProOrderRequest,
  CreateTrainerProOrderResponse,
  TrainerProOfferAdminDto,
  TrainerProOfferDto,
  UpdateTrainerProOfferRequest,
} from "./types";

const IDEMPOTENCY_HEADER = "Idempotency-Key";

export const trainerProApi = {
  /** Публичный (анонимный) список покупаемых вариантов подписки тренажёра. */
  getOffer: async ({ signal }: { signal?: AbortSignal } = {}) => {
    const res = await apiClient.get<Envelope<TrainerProOfferDto[]>>("/access/trainer-pro/offer/", {
      signal,
    });
    return res.data;
  },

  /**
   * Создаёт заказ на подписку тренажёра + дёргает T-Bank Init → `{orderId, paymentUrl}`.
   * `idempotencyKey` (UUID) передаётся header'ом — повторный POST с тем же ключом вернёт тот же
   * закешированный ответ (защита от двойного клика / retry'ев).
   */
  createOrder: async (request: CreateTrainerProOrderRequest, idempotencyKey: string) => {
    const res = await apiClient.post<Envelope<CreateTrainerProOrderResponse>>(
      "/access/trainer-pro/orders/",
      request,
      { headers: { [IDEMPOTENCY_HEADER]: idempotencyKey } },
    );
    return res.data;
  },

  // --- Admin offer management (perm plans.manage) ---

  getAdminOffer: async ({ signal }: { signal?: AbortSignal } = {}) => {
    const res = await apiClient.get<Envelope<TrainerProOfferAdminDto[]>>(
      "/access/admin/trainer-pro/offer/",
      { signal },
    );
    return res.data;
  },

  createOffer: async (request: CreateTrainerProOfferRequest) => {
    const res = await apiClient.post<Envelope<string>>("/access/admin/trainer-pro/offer/", request);
    return res.data;
  },

  updateOffer: async (planId: string, request: UpdateTrainerProOfferRequest) => {
    const res = await apiClient.patch<Envelope<string>>(
      `/access/admin/trainer-pro/offer/${planId}/`,
      request,
    );
    return res.data;
  },
};

/** Публичный оффер для лендинга / пейволла / subscribe-CTA. */
export const trainerProOfferQueryOptions = () =>
  queryOptions({
    queryKey: ["access", "trainer-pro", "offer"],
    queryFn: ({ signal }) => trainerProApi.getOffer({ signal }),
    select: (data) => data.result ?? [],
  });

/** Admin-список офферов (включая черновые) — страница управления подпиской тренажёра. */
export const trainerProAdminOfferQueryOptions = () =>
  queryOptions({
    queryKey: ["access", "trainer-pro", "admin", "offer"],
    queryFn: ({ signal }) => trainerProApi.getAdminOffer({ signal }),
    select: (data) => data.result ?? [],
  });

/** Ключ admin-офферов — для инвалидации после create/update. */
export const trainerProAdminOfferKey = ["access", "trainer-pro", "admin", "offer"] as const;
