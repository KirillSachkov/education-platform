/**
 * Генерация slug для мок-собеса (#585). Бэкенд требует непустой уникальный slug,
 * но автор задаёт только название — часто кириллицей. Латиницу нормализуем в
 * kebab-case, к любому результату добавляем короткий рандомный суффикс: для
 * кириллических названий ASCII-часть пуста (иначе slug был бы пустой → 400),
 * а суффикс заодно снимает риск коллизии при одинаковых названиях.
 */
export function slugifyMockInterview(title: string): string {
  const base = title
    .toLowerCase()
    .normalize("NFKD")
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "")
    .slice(0, 48);
  const suffix = Math.random().toString(36).slice(2, 8);
  return base ? `${base}-${suffix}` : `mock-${suffix}`;
}
