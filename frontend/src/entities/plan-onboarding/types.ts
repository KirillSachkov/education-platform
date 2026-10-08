export type OnboardingStepType =
  | "MARKDOWN"
  | "TELEGRAM"
  | "GITHUB"
  | "NOTIFICATIONS"
  | "GITHUB_REVIEW_APP";

export type OnboardingStepDto = {
  id: string;
  stepType: OnboardingStepType;
  isSkippable: boolean;
  sortOrder: string;
  title: string | null;
  body: string | null;
};

export type UserOnboardingStateDto = {
  startedAt: string;
  completedAt: string | null;
  currentStepId: string | null;
  skippedStepIds: string[];
  completedStepIds: string[];
};

export type CurrentOnboardingResponse = {
  planId: string;
  planDisplayName: string;
  planAuthorId: string;
  /**
   * GitHub-org slug плана (если автор настроил автоматический инвайт через GitHub App).
   * `null` — нет интеграции, GITHUB-step должен показать «свяжитесь с автором».
   * Если совпадает с одной из `MyProfile.githubOrgs` — юзер уже состоит, показываем
   * это сразу без отправки повторного invite.
   */
  planGitHubOrgSlug: string | null;
  steps: OnboardingStepDto[];
  state: UserOnboardingStateDto;
};

export type OnboardingFlowResponse = {
  planId: string;
  isEnabled: boolean;
  createdAt: string;
  updatedAt: string;
  steps: OnboardingStepDto[];
};

export type AddMarkdownStepRequest = {
  title: string;
  body: string;
  isSkippable: boolean;
};

export type UpdateMarkdownStepRequest = AddMarkdownStepRequest;

export type ReorderStepRequest = {
  beforeStepId: string | null;
  afterStepId: string | null;
};

export type SetOnboardingEnabledRequest = {
  isEnabled: boolean;
};

export type ToggleGithubReviewAppStepRequest = {
  isEnabled: boolean;
};

export type GithubInvitationStatus =
  | "PENDING"
  | "ACCEPTED"
  | "EXPIRED"
  | "CANCELED"
  | "FAILED";

export type GithubInvitationStatusResponse = {
  id: string;
  status: GithubInvitationStatus;
  orgLogin: string;
  githubLogin: string;
  failureReason: string | null;
  createdAt: string;
  acceptedAt: string | null;
  lastSyncedAt: string | null;
};

export type CreateGithubInvitationRequest = {
  planId: string;
  githubLogin: string;
};

export type InstallRedirectResponse = {
  url: string;
};

export type GithubInstallationStatusResponse = {
  isInstalled: boolean;
  orgLogin: string | null;
  isSuspended: boolean;
  installedAt: string | null;
};

export type PlanChatDto = {
  chatId: string;
  title: string | null;
  joinUrl: string | null;
  chatType: string;
  enrollmentGrantsMembership: boolean;
};

export type TelegramOnboardingStatusResponse = {
  isLinked: boolean;
  telegramUsername: string | null;
  chats: PlanChatDto[];
};

/** `POST /access/plans/{planId}/onboarding-flow/reset-all/` — bulk reset для всех grant-holder'ов. */
export type ResetAllOnboardingsResponse = {
  resetCount: number;
};

/** `POST /access/onboarding/{planId}/steps/telegram/recheck/` — user-triggered membership recheck. */
export type RecheckTelegramMembershipResponse = {
  completed: boolean;
  /** "member" | "not_member" | "unknown" — verbatim из TelegramBotService. */
  status: string;
};
