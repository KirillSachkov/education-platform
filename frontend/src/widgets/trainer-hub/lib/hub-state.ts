/**
 * URL-контракт хаба тренажёра (#568): `?track=<slug>&tab=<tab>`
 * + расширения под-режима режима «Обучение» (`?topic=<topicId>&sub=<sub>`).
 * Всё состояние верхнего уровня живёт в query — deep-link и кнопка «назад»
 * работают. Здесь — имена параметров, дефолты и парсеры. Не импортирует React.
 */

/**
 * Вкладки хаба, mode-centric (#568 Ф2). Порядок = порядок в табах. Первичные
 * {study,mock}, вторичные {progress,mistakes,bookmarks} — на мобиле вторичные
 * уходят за горизонтальный скролл. Вкладка «Тесты» слита в «Обучение» (под-режим
 * `test`) — тема открывается один раз, а формат (вопросы/тренировка/тест) выбирается
 * сегментом внутри.
 */
export const HUB_TABS = ["study", "mock", "progress", "mistakes", "bookmarks"] as const;

export type HubTab = (typeof HUB_TABS)[number];

/** Первичные (всегда видимые без скролла) и вторичные вкладки — для подсказок вёрстки. */
export const HUB_PRIMARY_TABS: readonly HubTab[] = ["study", "mock"];

export const HUB_DEFAULT_TAB: HubTab = "study";

/** Query-параметры хаба. */
export const HUB_PARAM = {
  track: "track",
  tab: "tab",
  /** Выбранная тема в режиме «Обучение» (topicId). */
  topic: "topic",
  /** Под-режим «Обучения»: `list` (вопросы) | `learn` (тренировка) | `test` (тест). */
  sub: "sub",
} as const;

/** Под-режим вкладки «Обучение»: вопросы / тренировка / тест (grade-at-end). */
export const STUDY_SUBMODES = ["list", "learn", "test"] as const;

export type StudySubmode = (typeof STUDY_SUBMODES)[number];

export const STUDY_DEFAULT_SUBMODE: StudySubmode = "list";

export function parseHubTab(raw: string | null): HubTab {
  return HUB_TABS.includes(raw as HubTab) ? (raw as HubTab) : HUB_DEFAULT_TAB;
}

export function parseStudySubmode(raw: string | null): StudySubmode {
  return STUDY_SUBMODES.includes(raw as StudySubmode)
    ? (raw as StudySubmode)
    : STUDY_DEFAULT_SUBMODE;
}
