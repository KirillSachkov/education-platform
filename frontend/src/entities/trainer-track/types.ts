/**
 * Типы треков тренажёра — зеркалят C#-контракт `TrainerService.Contracts.Tracks`.
 * ASP.NET сериализует свойства camelCase, `Stack` — строкой UPPER_SNAKE_CASE.
 * Issue #568.
 */

/**
 * Трек в студенческом верхнем селекторе хаба: метаданные + число опубликованных
 * тем. Зеркало `TrackDto`. Метаданные треков не gated (как каталог курсов).
 */
export interface TrainerTrack {
  id: string;
  slug: string;
  title: string;
  /** Стек трека: `CSHARP | TYPESCRIPT | DEVOPS` (extensible). */
  stack: string;
  description: string | null;
  /** Сколько опубликованных тем под треком. */
  topicCount: number;
}
