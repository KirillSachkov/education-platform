/**
 * Единый источник правды по UI-словарю тренажёра (#568): стеки треков,
 * направления тем, уровни сложности вопроса (J/M/S бейджи). Регистр совпадает
 * с C#-enum'ами на бэке — храним/шлём строго в UPPER_SNAKE_CASE.
 *
 * Лежит в `shared/config` (не в entities), чтобы и `shared/ui`-компоненты, и
 * виджеты/фичи могли импортировать без upward-зависимости (как `course-kind`).
 */

/**
 * Максимальная длительность ОДНОГО голосового ответа в тренажёре, в секундах (#663). Рекордер
 * авто-останавливает запись на этом пределе, а UI показывает «до N мин». Зеркалит серверный
 * `TrainerAI:MaxVoiceAnswerSeconds` (тот же дефолт 180s); сервер держит байтовый cap + nginx
 * `client_max_body_size 25m` на `/api/trainer/` как backstop. Менять оба значения вместе.
 */
export const MAX_VOICE_ANSWER_SECONDS = 180;

/** Человекочитаемая подпись лимита голосового ответа, напр. «3 мин» (#663). Ceil — как байтовый
 *  backstop на бэке (`(MaxVoiceAnswerSeconds + 59) / 60`), чтобы подпись и серверное сообщение совпадали. */
export const MAX_VOICE_ANSWER_LABEL = `${Math.ceil(MAX_VOICE_ANSWER_SECONDS / 60)} мин`;

/** mm:ss из секунд (общий формат таймера тренажёра). */
export function formatTrainerClock(totalSec: number): string {
  const safe = Math.max(0, Math.floor(totalSec));
  const minutes = Math.floor(safe / 60);
  const seconds = safe % 60;
  return `${minutes}:${seconds.toString().padStart(2, "0")}`;
}

/** Стек трека — верхний сегмент-селектор хаба. Расширяемый (string без CHECK на бэке). */
export const TRAINER_STACKS = ["CSHARP", "TYPESCRIPT", "DEVOPS"] as const;

export type TrainerStack = (typeof TRAINER_STACKS)[number];

/** Короткая подпись стека для сегмент-таба (как его называет разработчик). */
export const TRAINER_STACK_LABELS: Record<string, string> = {
  CSHARP: "C#",
  TYPESCRIPT: "TypeScript",
  DEVOPS: "DevOps",
};

/**
 * Направление темы (facet внутри трека) — вторичный фильтр «Все / Backend /
 * Frontend / Fullstack». `GENERAL` не показываем отдельной кнопкой (он matchится
 * любым направлением), но используем при фильтрации.
 */
export const TRAINER_DIRECTIONS = ["BACKEND", "FRONTEND", "FULLSTACK", "GENERAL"] as const;

export type TrainerDirection = (typeof TRAINER_DIRECTIONS)[number];

export const TRAINER_DIRECTION_LABELS: Record<string, string> = {
  BACKEND: "Backend",
  FRONTEND: "Frontend",
  FULLSTACK: "Fullstack",
  GENERAL: "Общее",
};

/** Какие направления показываем кнопками фильтра (в порядке). `GENERAL` — без своей кнопки. */
export const TRAINER_DIRECTION_FILTERS = ["BACKEND", "FRONTEND", "FULLSTACK"] as const;

/**
 * Уровень вопроса (`QuestionDifficulty`: JUNIOR/MIDDLE/SENIOR) — бейдж уровня в
 * раннере и в разбивке результатов. Цвет НИКОГДА не единственный сигнал —
 * всегда в паре с текстовым `label`.
 */
export const TRAINER_DIFFICULTIES = ["JUNIOR", "MIDDLE", "SENIOR"] as const;

export type TrainerDifficulty = (typeof TRAINER_DIFFICULTIES)[number];

export interface TrainerDifficultyVisual {
  label: string;
  /** Tailwind-классы для пилюли бейджа (фон + текст), согласованы с палитрой платформы. */
  badgeClass: string;
}

export const TRAINER_DIFFICULTY_VISUALS: Record<string, TrainerDifficultyVisual> = {
  JUNIOR: {
    label: "Джуниор",
    badgeClass: "bg-green/10 text-green ring-1 ring-inset ring-green/25",
  },
  MIDDLE: {
    label: "Миддл",
    badgeClass: "bg-amber-500/10 text-amber-600 ring-1 ring-inset ring-amber-500/25 dark:text-amber-400",
  },
  SENIOR: {
    label: "Сеньор",
    badgeClass: "bg-destructive/10 text-destructive ring-1 ring-inset ring-destructive/25",
  },
};

/**
 * Визуал mastery-бара/значения по порогам (слабая < 60 — янтарь, 60..79 —
 * primary, ≥80 — зелёный). Единый хелпер, чтобы карточки/прогресс не плодили
 * свои тернарники.
 */
export function getMasteryTone(masteryPercent: number): string {
  if (masteryPercent >= 80) return "bg-green/80";
  if (masteryPercent >= 60) return "bg-primary/80";
  return "bg-amber-500/80";
}

/**
 * Stroke-цвет mastery-кольца по тем же порогам, что `getMasteryTone` (для
 * SVG-ринга в Duolingo-карточках темы). Возвращает Tailwind `stroke-*`,
 * `getMasteryTone` — `bg-*`; держим оба в одном файле, чтобы пороги не дрейфили.
 */
export function getMasteryStrokeTone(masteryPercent: number): string {
  if (masteryPercent >= 80) return "stroke-green";
  if (masteryPercent >= 60) return "stroke-primary";
  return "stroke-amber-500";
}

/**
 * Фон-поверхность карточки темы по статусу освоения (#585): едва уловимый оттенок,
 * подсказывающий статус и подкрепляющий кольцо — зелёный для ОСВОЕННЫХ (mastery ≥80,
 * тот же порог, что зелёное кольцо), тёплый для СЛАБЫХ, нейтральный (`bg-card`) для
 * остальных (новые / в процессе). Ховер углубляет ТОТ ЖЕ оттенок (не нейтральный —
 * иначе статус «слетает» при наведении). Тихо намеренно: цвет не единственный сигнал
 * (есть кольцо % + пилюля «слабая»). Освоенность показываем только тут (mastered-тема
 * без пилюли — фон и зелёное кольцо достаточны). Возвращает `bg-*` + `hover:bg-*`.
 */
export function getTopicStatusSurface(
  masteryPercent: number,
  isWeak: boolean,
  showMastery: boolean,
): string {
  if (showMastery && masteryPercent >= 80) return "bg-green/[0.07] hover:bg-green/[0.12]";
  if (isWeak) return "bg-amber-500/[0.07] hover:bg-amber-500/[0.12]";
  return "bg-card hover:bg-muted";
}

/**
 * Статус изучения вопроса (`QuestionStudyStatus`, Ф2): `NEW` (нет строки) →
 * `SEEN` → `KNOWN` / `REVIEW` / `WRONG`. Чип-метка в списке вопросов, «Моих
 * ошибках», SRS. Текстовая (де-иконенный стиль) — цвет НИКОГДА не единственный
 * сигнал, всегда с подписью.
 */
export interface TrainerStudyStatusVisual {
  label: string;
  /** Tailwind-классы пилюли (фон + текст). `null` для NEW — рендерим без чипа. */
  chipClass: string | null;
}

export const TRAINER_STUDY_STATUS_VISUALS: Record<string, TrainerStudyStatusVisual> = {
  NEW: { label: "Новый", chipClass: null },
  SEEN: { label: "Видел", chipClass: "bg-muted text-muted-foreground" },
  KNOWN: { label: "Знаю", chipClass: "bg-green/10 text-green" },
  REVIEW: {
    label: "На повтор",
    chipClass: "bg-amber-500/10 text-amber-600 dark:text-amber-400",
  },
  WRONG: { label: "Ошибка", chipClass: "bg-destructive/10 text-destructive" },
};

/**
 * Реестр «концепт стата → иконка» (#568). ЕДИНСТВЕННОЕ место, где задаётся,
 * какой иконкой обозначается каждый показатель в тренажёре — чтобы «если стат
 * обозначается так, он так обозначается ВЕЗДЕ» (хаб, дашборд статистики, баннеры).
 * Значения — ключи реестра `shared/ui/icons` (`Icons[key]`), не lucide напрямую.
 */
export const TRAINER_STAT_ICONS = {
  mastery: "trending", // общий уровень освоения / тренд
  accuracy: "target", // точность ответов
  streak: "streak", // дней подряд
  due: "clock", // SRS «на повтор сегодня»
  activity: "calendar", // активность по дням
  answered: "check", // отвечено вопросов
  coverage: "chart", // покрытие материала
  mock: "briefcase", // мок-собесы
  achievement: "trophy", // достижения/итоги
} as const;

export type TrainerStatConcept = keyof typeof TRAINER_STAT_ICONS;

/**
 * Класс «материальной карточки» тренажёра (#568): тонкий бордер + радиус +
 * едва уловимый верхний sheen (см. `.t-card` в globals.css). НЕ задаёт фон —
 * композится с `bg-card` или `getTopicStatusSurface()`, чтобы оттенки не дрались.
 * Каскадный вход — добавь к элементу `t-enter` и `style={{ "--t-i": index }}`.
 */
export const TRAINER_CARD_SURFACE = "t-card";

/**
 * Длительность count-up чисел (мс) — синхронна `--t-fill-dur` заполнения колец,
 * чтобы число и кольцо «доезжали» вместе. Хук `useCountUp` уважает
 * `prefers-reduced-motion` (мгновенно показывает финал).
 */
export const TRAINER_COUNT_UP_MS = 700;

/**
 * Цвет верхнего sheen карточки (`--t-tint`) по статусу освоения — в тон порогам
 * `getMasteryStrokeTone` (≥80 зелёный / слабая тёплый / нейтраль). `undefined` →
 * нейтральный foreground-wash из `.t-card`. Возвращает CSS-значение для инлайн-стиля.
 */
export function getTrainerCardTint(
  masteryPercent: number,
  isWeak: boolean,
  showMastery: boolean,
): string | undefined {
  if (showMastery && masteryPercent >= 80) return "var(--green)";
  if (isWeak) return "oklch(0.769 0.188 70.08)"; // amber-500 (нет theme-токена амбера)
  return undefined;
}
