export type VcsInstallationStatus = "ACTIVE" | "SUSPENDED" | "UNINSTALLED";

export type VcsInstallationOwnerType = "USER" | "ORG";

export type VcsProvider = "GITHUB";

/**
 * Snapshot пользовательской установки GitHub App, на котором работает
 * AssignmentReviewService (AI-проверка PR'ов). Backend контракт —
 * `AssignmentReviewService.Contracts.Installations.VcsInstallationDto`.
 */
export type VcsInstallation = {
  id: string;
  provider: VcsProvider;
  ownerLogin: string;
  ownerType: VcsInstallationOwnerType;
  status: VcsInstallationStatus;
  allRepos: boolean;
  repos: string[];
  installedAt: string;
  removedAt: string | null;
};

export type MyInstallationsResponse = {
  installations: VcsInstallation[];
};

export type StartInstallationRequest = {
  returnUrl?: string;
};

export type StartInstallationResponse = {
  redirectUrl: string;
  state: string;
};

/**
 * Конкретные status'ы (для UI-копий + цветовой схемы). Backend отдаёт
 * `Status` в нужном виде, парсить не надо.
 */
export const VCS_INSTALLATION_STATUSES: VcsInstallationStatus[] = [
  "ACTIVE",
  "SUSPENDED",
  "UNINSTALLED",
];
