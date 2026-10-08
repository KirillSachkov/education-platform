// Тонкий re-export над entities/vcs-installation — оставлен для
// backward-compat имени `connectReviewAppApi`, под которым реализован
// существующий ReviewAppCard. Новые потребители (onboarding-wizard) идут
// напрямую в entity layer.
export { vcsInstallationApi as connectReviewAppApi } from "@/entities/vcs-installation";
export type {
  StartInstallationRequest,
  StartInstallationResponse,
} from "@/entities/vcs-installation";
