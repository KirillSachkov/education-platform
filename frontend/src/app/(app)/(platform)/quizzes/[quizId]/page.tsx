import type { Metadata } from "next";
import { StudentQuizPage } from "@/features/quiz-runner";
import { buildEntityMetadata, fetchAnonymous } from "@/shared/seo";

interface Props {
  params: Promise<{ quizId: string }>;
}

interface QuizPreview {
  title?: string;
}

export async function generateMetadata({ params }: Props): Promise<Metadata> {
  const { quizId } = await params;
  // PUBLIC-квиз отдаётся анониму; гейтнутый/несуществующий → null → fallback.
  const quiz = await fetchAnonymous<QuizPreview>(`/quizzes/${quizId}/student/`);
  if (!quiz?.title) return { title: "Тест" };

  return buildEntityMetadata({
    title: quiz.title,
    description: "Тест на SachkovLearn",
    path: `/quizzes/${quizId}`,
    fullTitle: `${quiz.title} — тест на SachkovLearn`,
  });
}

/**
 * Студенческая страница standalone-квиза (ST-16 #495) — цель quiz-строк
 * программы курса и подборок. Состояния доступа/404 разруливает клиентский
 * компонент по ответу `GET /quizzes/{id}/student/`.
 */
export default async function QuizPage({ params }: Props) {
  const { quizId } = await params;
  return <StudentQuizPage quizId={quizId} />;
}
