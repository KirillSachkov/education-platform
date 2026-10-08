/**
 *  Извлекает GitHub username из URL вида `https://github.com/{login}`.
 *  Возвращает `null` если URL пустой / не GitHub / без login segment.
 *  Используется в GithubStepView чтобы получить login юзера для invitation API.
 */
export function extractGithubLogin(githubUrl: string | null): string | null {
  if (!githubUrl) return null;
  const match = githubUrl.match(/github\.com\/([^/?#]+)/);
  return match?.[1] ?? null;
}
