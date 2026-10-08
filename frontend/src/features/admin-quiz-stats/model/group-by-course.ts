import type { QuizAdminOverviewRow } from "../api";

export type CourseQuizGroup = {
  courseId: string | null;
  courseTitle: string;
  quizzes: QuizAdminOverviewRow[];
};

const NO_COURSE_LABEL = "Без курса";

/**
 * Группирует строки overview по курсу (client-side, #556 AC5). Группы сортируются
 * по суммарному числу попыток (самые активные курсы сверху), standalone-тесты
 * («Без курса») — всегда последними. Внутри группы строки сохраняют порядок из
 * overview (по убыванию попыток).
 */
export function groupQuizzesByCourse(rows: QuizAdminOverviewRow[]): CourseQuizGroup[] {
  const byCourse = new Map<string, CourseQuizGroup>();

  for (const row of rows) {
    const key = row.courseId ?? "__none__";
    let group = byCourse.get(key);
    if (!group) {
      group = {
        courseId: row.courseId,
        courseTitle: row.courseTitle ?? NO_COURSE_LABEL,
        quizzes: [],
      };
      byCourse.set(key, group);
    }
    group.quizzes.push(row);
  }

  return [...byCourse.values()].sort((a, b) => {
    // «Без курса» всегда в конце.
    if (a.courseId === null) return 1;
    if (b.courseId === null) return -1;
    const aAttempts = a.quizzes.reduce((sum, q) => sum + q.attemptsCount, 0);
    const bAttempts = b.quizzes.reduce((sum, q) => sum + q.attemptsCount, 0);
    return bAttempts - aAttempts;
  });
}
