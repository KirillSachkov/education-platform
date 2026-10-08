/**
 * Генерация slug для темы тренажёра (#623). Бэкенд требует непустой уникальный
 * slug из строчных латинских букв, цифр и дефиса (`trainer.topic.slug.invalid`),
 * но автор задаёт slug руками (поле в форме). Этот хелпер лишь подсказывает
 * стартовое значение из названия: латиницу нормализуем в kebab-case, для
 * кириллических названий ASCII-часть пуста → добавляем короткий рандомный
 * суффикс, чтобы slug не оказался пустым и не коллизил.
 */
export function suggestTopicSlug(title: string): string {
  const base = title
    .toLowerCase()
    .normalize("NFKD")
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "")
    .slice(0, 48);
  if (base) return base;
  const suffix = Math.random().toString(36).slice(2, 8);
  return `topic-${suffix}`;
}
