/**
 * Гейт показа blur-плейсхолдера для редактированного сервером контента тренажёра (#674).
 *
 * Для заблокированного (`isLocked`) вопроса не-PRO пользователя сервер НЕ присылает текст
 * (`questionText`/`stem === null`, `options: []`) — утечки контента нет. Фронт рисует размытый
 * скелетон-плейсхолдер вместо markdown'а, а не падает на `null`/пустой строке.
 *
 * Возвращает `true`, когда показывать плейсхолдер: вопрос заблокирован ЛИБО контент пуст
 * (null / пустая строка / только пробелы) — defensive, чтобы любой редактированный ответ
 * не уронил рендер.
 */
export function isTrainerContentRedacted(
  isLocked: boolean,
  content: string | null | undefined,
): boolean {
  return isLocked || content == null || content.trim().length === 0;
}
