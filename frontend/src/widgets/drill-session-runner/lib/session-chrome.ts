import type { TrainerSessionMode } from "@/entities/trainer-session";
import { routes } from "@/shared/config/routes";

export interface SessionChromeInput {
  mode: TrainerSessionMode;
  topicIds: string[];
  /** Явный URL «К теме» — имеет приоритет над выводом из режима. */
  backHref?: string;
  /** Явный ярлык режима в шапке — имеет приоритет над выводом из режима. */
  modeLabelOverride?: string;
}

export interface SessionChrome {
  modeLabel: string;
  backHref: string;
}

/**
 * Ярлык режима + цель «К теме» для шапки раннера. По умолчанию выводятся из
 * `mode`: тест (DRILL/CHALLENGE) | тренировка (LEARN) | симуляция (MOCK).
 *
 * Review-сессии (#656) — клик по вопросу, «Доучить», SRS, закладки — на бэке
 * тоже `LEARN`, поэтому по `mode` неотличимы от пачки-тренировки. Точка запуска
 * передаёт явные `backHref` (вернуться ровно во вкладку, откуда пришли) и
 * `modeLabelOverride` (не «Тренировка»), и они побеждают вывод из режима.
 */
export function resolveSessionChrome({
  mode,
  topicIds,
  backHref,
  modeLabelOverride,
}: SessionChromeInput): SessionChrome {
  const isMock = mode === "MOCK";
  const modeLabel =
    modeLabelOverride ?? (isMock ? "Симуляция" : mode === "LEARN" ? "Тренировка" : "Тест");
  const exitTopicId = topicIds[0];
  const resolvedBackHref =
    backHref ??
    (isMock
      ? `${routes.trainer}?tab=mock`
      : exitTopicId
        ? `${routes.trainer}?tab=study&topic=${exitTopicId}&sub=${mode === "LEARN" ? "learn" : "test"}`
        : routes.trainer);
  return { modeLabel, backHref: resolvedBackHref };
}
