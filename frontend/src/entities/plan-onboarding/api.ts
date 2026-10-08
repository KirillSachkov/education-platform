import { apiClient, type Envelope } from "@/shared/api";
import { queryOptions } from "@tanstack/react-query";
import type {
  AddMarkdownStepRequest,
  CreateGithubInvitationRequest,
  CurrentOnboardingResponse,
  GithubInstallationStatusResponse,
  GithubInvitationStatusResponse,
  InstallRedirectResponse,
  OnboardingFlowResponse,
  RecheckTelegramMembershipResponse,
  ReorderStepRequest,
  ResetAllOnboardingsResponse,
  SetOnboardingEnabledRequest,
  TelegramOnboardingStatusResponse,
  ToggleGithubReviewAppStepRequest,
  UpdateMarkdownStepRequest,
} from "./types";

export const planOnboardingApi = {
  // student-facing
  getCurrent: async ({ signal }: { signal?: AbortSignal } = {}) => {
    const res = await apiClient.get<Envelope<CurrentOnboardingResponse | null>>(
      "/access/onboarding/current/",
      { signal },
    );
    return res.data;
  },

  skipStep: async (planId: string, stepId: string) => {
    const res = await apiClient.post<Envelope<string>>(
      `/access/onboarding/${planId}/steps/${stepId}/skip/`,
    );
    return res.data;
  },

  completeStep: async (planId: string, stepId: string) => {
    const res = await apiClient.post<Envelope<string>>(
      `/access/onboarding/${planId}/steps/${stepId}/complete/`,
    );
    return res.data;
  },

  returnToStep: async (planId: string, stepId: string) => {
    const res = await apiClient.post<Envelope<string>>(
      `/access/onboarding/${planId}/steps/${stepId}/return-to/`,
    );
    return res.data;
  },

  completeOnboarding: async (planId: string) => {
    const res = await apiClient.post<Envelope<string>>(
      `/access/onboarding/${planId}/complete/`,
    );
    return res.data;
  },

  resetOnboarding: async (planId: string) => {
    const res = await apiClient.post<Envelope<string>>(
      `/access/onboarding/${planId}/reset/`,
    );
    return res.data;
  },

  // admin-facing
  getFlow: async (planId: string, { signal }: { signal?: AbortSignal } = {}) => {
    const res = await apiClient.get<Envelope<OnboardingFlowResponse>>(
      `/access/plans/${planId}/onboarding-flow/`,
      { signal },
    );
    return res.data;
  },

  setEnabled: async (planId: string, request: SetOnboardingEnabledRequest) => {
    const res = await apiClient.put<Envelope<boolean>>(
      `/access/plans/${planId}/onboarding-flow/`,
      request,
    );
    return res.data;
  },

  addMarkdownStep: async (planId: string, request: AddMarkdownStepRequest) => {
    const res = await apiClient.post<Envelope<string>>(
      `/access/plans/${planId}/onboarding-flow/steps/`,
      request,
    );
    return res.data;
  },

  updateMarkdownStep: async (
    planId: string,
    stepId: string,
    request: UpdateMarkdownStepRequest,
  ) => {
    const res = await apiClient.patch<Envelope<string>>(
      `/access/plans/${planId}/onboarding-flow/steps/${stepId}/`,
      request,
    );
    return res.data;
  },

  reorderStep: async (planId: string, stepId: string, request: ReorderStepRequest) => {
    const res = await apiClient.patch<Envelope<string>>(
      `/access/plans/${planId}/onboarding-flow/steps/${stepId}/order/`,
      request,
    );
    return res.data;
  },

  deleteStep: async (planId: string, stepId: string) => {
    const res = await apiClient.delete<Envelope<string>>(
      `/access/plans/${planId}/onboarding-flow/steps/${stepId}/`,
    );
    return res.data;
  },

  setStepIsSkippable: async (planId: string, stepId: string, isSkippable: boolean) => {
    const res = await apiClient.patch<Envelope<boolean>>(
      `/access/plans/${planId}/onboarding-flow/steps/${stepId}/skippable/`,
      { isSkippable },
    );
    return res.data;
  },

  toggleGithubReviewAppStep: async (
    planId: string,
    request: ToggleGithubReviewAppStepRequest,
  ) => {
    const res = await apiClient.post<Envelope<boolean>>(
      `/access/plans/${planId}/onboarding-flow/steps/github-review-app/`,
      request,
    );
    return res.data;
  },

  resetAllOnboardings: async (planId: string) => {
    const res = await apiClient.post<Envelope<ResetAllOnboardingsResponse>>(
      `/access/plans/${planId}/onboarding-flow/reset-all/`,
    );
    return res.data;
  },
};

export const currentOnboardingQueryOptions = queryOptions({
  queryKey: ["plan-onboarding", "current"],
  queryFn: ({ signal }) => planOnboardingApi.getCurrent({ signal }),
  select: (data) => data.result ?? null,
  staleTime: 30_000,
});

export const onboardingFlowQueryOptions = (planId: string) =>
  queryOptions({
    queryKey: ["plan-onboarding", "flow", planId],
    queryFn: ({ signal }) => planOnboardingApi.getFlow(planId, { signal }),
    select: (data) => data.result!,
  });

// GitHub App-related API
export const githubAppApi = {
  getInstallRedirect: async (request: { planId?: string } = {}) => {
    const res = await apiClient.post<Envelope<InstallRedirectResponse>>(
      "/access/integrations/github/install-redirect/",
      { planId: request.planId ?? null },
    );
    return res.data;
  },

  createInvitation: async (request: CreateGithubInvitationRequest) => {
    const res = await apiClient.post<Envelope<GithubInvitationStatusResponse>>(
      "/access/integrations/github/invitations/",
      request,
    );
    return res.data;
  },

  getInvitationStatus: async (
    planId: string,
    { signal }: { signal?: AbortSignal } = {},
  ) => {
    const res = await apiClient.get<Envelope<GithubInvitationStatusResponse | null>>(
      `/access/integrations/github/invitations/${planId}/`,
      { signal },
    );
    return res.data;
  },

  syncInvitation: async (invitationId: string) => {
    const res = await apiClient.post<Envelope<GithubInvitationStatusResponse>>(
      `/access/integrations/github/invitations/${invitationId}/sync/`,
    );
    return res.data;
  },

  getInstallationStatus: async ({ signal }: { signal?: AbortSignal } = {}) => {
    const res = await apiClient.get<Envelope<GithubInstallationStatusResponse>>(
      "/access/integrations/github/installation-status/",
      { signal },
    );
    return res.data;
  },
};

export const githubInvitationStatusQueryOptions = (planId: string) =>
  queryOptions({
    queryKey: ["plan-onboarding", "github-invitation", planId],
    queryFn: ({ signal }) => githubAppApi.getInvitationStatus(planId, { signal }),
    select: (data) => data.result ?? null,
    staleTime: 5_000,
  });

export const githubInstallationStatusQueryOptions = queryOptions({
  queryKey: ["plan-onboarding", "github-installation-status"],
  queryFn: ({ signal }) => githubAppApi.getInstallationStatus({ signal }),
  select: (data) => data.result!,
  staleTime: 30_000,
});

export const telegramOnboardingApi = {
  getStatus: async (planId: string, { signal }: { signal?: AbortSignal } = {}) => {
    const res = await apiClient.get<Envelope<TelegramOnboardingStatusResponse>>(
      `/telegram/me/onboarding-status/?planId=${planId}`,
      { signal },
    );
    return res.data;
  },

  recheckMembership: async (planId: string) => {
    const res = await apiClient.post<Envelope<RecheckTelegramMembershipResponse>>(
      `/access/onboarding/${planId}/steps/telegram/recheck/`,
    );
    return res.data;
  },
};

export const telegramOnboardingStatusQueryOptions = (planId: string) =>
  queryOptions({
    queryKey: ["plan-onboarding", "telegram-status", planId],
    queryFn: ({ signal }) => telegramOnboardingApi.getStatus(planId, { signal }),
    select: (data) => data.result!,
    // Без staleTime / refetchOnMount: юзер мог привязать TG ВНЕ wizard'а
    // (через settings или другой план), и при возврате в wizard статус
    // должен подтянуться. 30s stale-кэш приводил к "Подключи Telegram"
    // вечно, пока не истечёт.
    staleTime: 0,
    refetchOnMount: "always",
  });
