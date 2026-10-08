import { apiClient, type Envelope } from "@/shared/api";
import { queryOptions } from "@tanstack/react-query";
import type {
  CompleteProfileRequest,
  MyProfile,
  PublicProfile,
  UpdateMyAccountInfoRequest,
  UpdateMyAuthorProfileRequest,
  UpdateMyBaseProfileRequest,
  UpdateMyReviewerProfileRequest,
} from "./types";

export const profileApi = {
  getMyProfile: async ({ signal }: { signal?: AbortSignal } = {}) => {
    const res = await apiClient.get<Envelope<MyProfile>>("/users/me", { signal });

    return res.data;
  },

  updateMyBaseProfile: async (request: UpdateMyBaseProfileRequest) => {
    const res = await apiClient.patch<Envelope<string>>("/users/me/base", request);

    return res.data;
  },

  updateMyAuthorProfile: async (request: UpdateMyAuthorProfileRequest) => {
    const res = await apiClient.patch<Envelope<string>>("/users/me/author", request);

    return res.data;
  },

  updateMyReviewerProfile: async (request: UpdateMyReviewerProfileRequest) => {
    const res = await apiClient.patch<Envelope<string>>("/users/me/reviewer", request);

    return res.data;
  },

  updateMyAccountInfo: async (request: UpdateMyAccountInfoRequest) => {
    const res = await apiClient.patch<Envelope<string>>("/users/me/account", request);

    return res.data;
  },

  completeProfile: async (request: CompleteProfileRequest) => {
    const res = await apiClient.post<Envelope<string>>("/users/me/profile/complete", request);

    return res.data;
  },

  checkUsernameAvailability: async (
    username: string,
    { signal }: { signal?: AbortSignal } = {},
  ) => {
    const res = await apiClient.get<Envelope<{ isAvailable: boolean }>>("/users/check-username", {
      params: { username },
      signal,
    });

    return res.data;
  },
};

export const profileQueryOptions = {
  baseKey: "profile",
  publicProfileKey: "public-profile",

  getMyProfileKey: () => [profileQueryOptions.baseKey, "me"] as const,
  getMyProfileOptions: () => {
    return queryOptions({
      queryKey: profileQueryOptions.getMyProfileKey(),
      queryFn: ({ signal }) => profileApi.getMyProfile({ signal }),
      select: (data) => data.result,
    });
  },
};

export const publicProfileQueryOptions = (userId: string) =>
  queryOptions({
    queryKey: [profileQueryOptions.baseKey, profileQueryOptions.publicProfileKey, userId],
    queryFn: async ({ signal }) => {
      const res = await apiClient.get<Envelope<PublicProfile>>(`/users/${userId}/public-profile/`, {
        signal,
      });
      return res.data;
    },
    select: (data) => data.result!,
    staleTime: 5 * 60 * 1000,
    enabled: !!userId,
  });
