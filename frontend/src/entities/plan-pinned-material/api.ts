import { apiClient, type Envelope } from "@/shared/api";
import { queryOptions } from "@tanstack/react-query";
import type {
  AddHomePinRequest,
  HomePinDto,
  HomePinListItemDto,
  ReorderHomePinRequest,
  UpdateHomePinNoteRequest,
} from "./types";

export const homePinsApi = {
  // student-facing — merged pins of the caller's active plans
  getMine: async ({ signal }: { signal?: AbortSignal } = {}) => {
    const res = await apiClient.get<Envelope<HomePinDto[]>>("/access/me/home-pins/", {
      signal,
    });
    return res.data;
  },

  // author-facing — pins of a single plan
  getForPlan: async (planId: string, { signal }: { signal?: AbortSignal } = {}) => {
    const res = await apiClient.get<Envelope<HomePinListItemDto[]>>(
      `/access/plans/${planId}/home-pins/`,
      { signal },
    );
    return res.data;
  },

  add: async (planId: string, request: AddHomePinRequest) => {
    const res = await apiClient.post<Envelope<string>>(
      `/access/plans/${planId}/home-pins/`,
      request,
    );
    return res.data;
  },

  updateNote: async (planId: string, pinId: string, request: UpdateHomePinNoteRequest) => {
    const res = await apiClient.patch<Envelope<string>>(
      `/access/plans/${planId}/home-pins/${pinId}/`,
      request,
    );
    return res.data;
  },

  reorder: async (planId: string, pinId: string, request: ReorderHomePinRequest) => {
    const res = await apiClient.post<Envelope<string>>(
      `/access/plans/${planId}/home-pins/${pinId}/order/`,
      request,
    );
    return res.data;
  },

  remove: async (planId: string, pinId: string) => {
    const res = await apiClient.delete<Envelope<string>>(
      `/access/plans/${planId}/home-pins/${pinId}/`,
    );
    return res.data;
  },
};

/** Query key factory — shared by the home section query and author mutations' invalidation. */
export const homePinsKeys = {
  mine: ["home-pins", "mine"] as const,
  forPlan: (planId: string) => ["home-pins", "plan", planId] as const,
};

export const myHomePinsQueryOptions = queryOptions({
  queryKey: homePinsKeys.mine,
  queryFn: ({ signal }) => homePinsApi.getMine({ signal }),
  select: (data) => data.result ?? [],
  staleTime: 60_000,
});

export const planHomePinsQueryOptions = (planId: string) =>
  queryOptions({
    queryKey: homePinsKeys.forPlan(planId),
    queryFn: ({ signal }) => homePinsApi.getForPlan(planId, { signal }),
    select: (data) => data.result ?? [],
  });
