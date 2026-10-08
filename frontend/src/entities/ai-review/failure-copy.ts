/**
 * Maps a backend AI-review failure code to user-facing Russian copy.
 * Shared between the iteration card and the student status banner so the
 * wording stays consistent across surfaces.
 */
export function resolveIterationFailureCopy(code: string): string {
  switch (code) {
    case "review.diff.too_large":
      return "PR большой — авто-проверка пропущена. Автор может запустить AI-проверку вручную: она разобьёт PR на части и проверит целиком.";
    case "review.no_installation":
      return "GitHub App не подключён к репозиторию. Подключи интеграцию.";
    case "review.repo.not_in_installation":
      return "Репозиторий не в whitelist GitHub App. Открой App доступ.";
    case "review.github.unavailable":
      return "GitHub недоступен. Попробуй позже.";
    case "review.github.invalid_request":
      return "GitHub отверг отзыв AI. Нажми «Перепроверить» — будет новая попытка.";
    case "review.llm.unavailable":
      return "AI-провайдер недоступен. Попробуй позже.";
    case "review.llm.invalid_output":
      return "AI вернул некорректный ответ. Попробуй ещё раз.";
    case "review.pr.draft_not_supported":
      return "PR в Draft. Нажми «Ready for review» в GitHub и отправь задачу снова.";
    case "review.rate_limit.exceeded":
      return "Превышен лимит AI-проверок. Попробуй позже.";
    case "review.cancelled":
      return "AI-проверка остановлена вручную.";
    default:
      return "Неизвестная ошибка AI-проверки. Попробуй ещё раз.";
  }
}
