import { apiClient, type Envelope } from "@/shared/api";
import { queryOptions } from "@tanstack/react-query";
import type {
  MyInstallationsResponse,
  StartInstallationRequest,
  StartInstallationResponse,
} from "./types";

export const vcsInstallationApi = {
  getMyInstallations: async ({ signal }: { signal?: AbortSignal } = {}) => {
    const res = await apiClient.get<Envelope<MyInstallationsResponse>>(
      "/assignment-review/installations/me/",
      { signal },
    );

    return res.data;
  },

  startInstallation: async (request: StartInstallationRequest = {}) => {
    const res = await apiClient.post<Envelope<StartInstallationResponse>>(
      "/assignment-review/installations/start/",
      request,
    );

    return res.data;
  },
};

export const myInstallationsQueryOptions = () =>
  queryOptions({
    queryKey: ["assignment-review", "installations", "me"],
    queryFn: ({ signal }) => vcsInstallationApi.getMyInstallations({ signal }),
    staleTime: 30_000,
  });
