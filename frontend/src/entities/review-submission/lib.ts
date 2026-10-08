/**
 * Распознаёт ссылку на GitHub pull request. Принимает каноничный PR URL
 * `https://github.com/{owner}/{repo}/pull/{N}` ПЛЮС любой хвост, который студент мог
 * вставить: trailing slash, `?query`, `#fragment` (например `#pullrequestreview-…`),
 * sub-path (`/files`, `/commits`, `/changes/<sha>`). Раньше строгий якорь `$` после
 * номера отбрасывал такие URL → AI-блок на карточке проверки прятался, а бэкенд
 * (`IssueSubmissionAwaitingReviewHandler`) не создавал AiReview. (#668)
 *
 * Зеркалит backend-регулярку в том же фиксе — держать в синхроне.
 */
export function isGitHubPullRequestUrl(value: string): boolean {
  return /^https:\/\/github\.com\/[^/]+\/[^/]+\/pull\/\d+(?:[/?#].*)?$/i.test(value.trim());
}
