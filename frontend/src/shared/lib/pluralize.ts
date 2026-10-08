/**
 * Russian pluralization helpers.
 *
 * Russian has three grammatical number forms for countable nouns:
 *   - one  — 1, 21, 31, ... (last digit 1, except 11)
 *   - few  — 2-4, 22-24, ... (last digit 2-4, except 12-14)
 *   - many — 0, 5-20, 25-30, ... (everything else)
 *
 * Implemented via `Intl.PluralRules("ru-RU")` for correctness on edge cases
 * (negative numbers, decimals, large values).
 */

export type RuPluralForms = {
  one: string;
  few: string;
  many: string;
};

// Lazy singleton — Intl.PluralRules is relatively cheap to instantiate but
// there's no reason to rebuild it on every call.
let cachedRules: Intl.PluralRules | null = null;

function getRules(): Intl.PluralRules {
  if (!cachedRules) {
    cachedRules = new Intl.PluralRules("ru-RU");
  }
  return cachedRules;
}

/**
 * Picks the correct Russian plural form for a given count.
 *
 * @example
 *   pluralize(5, "урок", "урока", "уроков") // → "уроков"
 *   pluralize(1, "курс", "курса", "курсов") // → "курс"
 *   pluralize(23, "день", "дня", "дней")   // → "дня"
 */
export function pluralize(n: number, one: string, few: string, many: string): string {
  const rule = getRules().select(Math.abs(n));
  if (rule === "one") return one;
  if (rule === "few") return few;
  return many;
}

/**
 * Picks the correct form from a `RuPluralForms` object.
 *
 * @example
 *   pluralizeRu(5, RU_PLURALS.lesson) // → "уроков"
 */
export function pluralizeRu(n: number, forms: RuPluralForms): string {
  return pluralize(n, forms.one, forms.few, forms.many);
}

/**
 * Formats a count with its Russian plural form in a single string.
 *
 * @example
 *   formatRuPlural(5, RU_PLURALS.lesson) // → "5 уроков"
 *   formatRuPlural(1, RU_PLURALS.course) // → "1 курс"
 */
export function formatRuPlural(n: number, forms: RuPluralForms): string {
  return `${n} ${pluralizeRu(n, forms)}`;
}

/**
 * Predefined plural dictionaries for common domain entities.
 *
 * Add new entries here rather than inlining `pluralize` calls — keeps the
 * vocabulary centralized and consistent across the app.
 */
export const RU_PLURALS = {
  // Learning entities
  course: { one: "курс", few: "курса", many: "курсов" },
  module: { one: "модуль", few: "модуля", many: "модулей" },
  lesson: { one: "урок", few: "урока", many: "уроков" },
  issue: { one: "задача", few: "задачи", many: "задач" },
  project: { one: "проект", few: "проекта", many: "проектов" },
  article: { one: "статья", few: "статьи", many: "статей" },
  element: { one: "элемент", few: "элемента", many: "элементов" },
  item: { one: "элемент", few: "элемента", many: "элементов" },
  roadmap: { one: "роадмап", few: "роадмапа", many: "роадмапов" },
  node: { one: "узел", few: "узла", many: "узлов" },

  // People
  student: { one: "студент", few: "студента", many: "студентов" },
  author: { one: "автор", few: "автора", many: "авторов" },
  user: { one: "пользователь", few: "пользователя", many: "пользователей" },

  // Content
  comment: { one: "комментарий", few: "комментария", many: "комментариев" },
  reply: { one: "ответ", few: "ответа", many: "ответов" },
  tag: { one: "тег", few: "тега", many: "тегов" },
  collection: { one: "подборка", few: "подборки", many: "подборок" },
  material: { one: "материал", few: "материала", many: "материалов" },
  plan: { one: "план", few: "плана", many: "планов" },

  // Time
  second: { one: "секунда", few: "секунды", many: "секунд" },
  minute: { one: "минута", few: "минуты", many: "минут" },
  hour: { one: "час", few: "часа", many: "часов" },
  day: { one: "день", few: "дня", many: "дней" },
  week: { one: "неделя", few: "недели", many: "недель" },
  month: { one: "месяц", few: "месяца", many: "месяцев" },
  year: { one: "год", few: "года", many: "лет" },

  // Gamification
  xp: { one: "очко", few: "очка", many: "очков" },
  level: { one: "уровень", few: "уровня", many: "уровней" },
} as const satisfies Record<string, RuPluralForms>;
