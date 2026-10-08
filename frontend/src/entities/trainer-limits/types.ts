/** Один лимит AI-использования: сколько использовано и каков потолок. `limit=null` ⇒ безлимит. */
export interface TrainerLimit {
  used: number;
  limit: number | null;
}

/**
 * Остаток AI-лимитов вызывающего + статус Trainer Pro (#568). Зеркало `TrainerLimitsDto`.
 * `openGrades` — дневной лимит проверок открытых ответов (в штуках); `voice` — месячный лимит в
 * МИНУТАХ аудио (#663, `used`/`limit` в минутах); `mock` — месячный лимит мок-собесов (в штуках).
 * У free всё = 0 (PRO-фичи). `isPro` включает авто-PRO за полный доступ к платформе.
 */
export interface TrainerLimits {
  isPro: boolean;
  openGrades: TrainerLimit;
  voice: TrainerLimit;
  mock: TrainerLimit;
}
