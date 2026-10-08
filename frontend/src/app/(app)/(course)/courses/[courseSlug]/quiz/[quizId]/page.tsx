import type { Metadata } from "next";
import { fetchAnonymous } from "@/shared/seo";
import { CourseQuizClient } from "./page-client";

interface Props {
  params: Promise<{ courseSlug: string; quizId: string }>;
}

interface QuizPreview {
  title?: string;
}

export async function generateMetadata({ params }: Props): Promise<Metadata> {
  const { quizId } = await params;
  // PUBLIC-квиз отдаёт title анониму; гейченный — null → нейтральный fallback.
  const quiz = await fetchAnonymous<QuizPreview>(`/quizzes/${quizId}/student/`);
  return { title: quiz?.title ?? "Тест" };
}

// Квиз в контексте курса (#495): CourseSidebar + breadcrumbs — по аналогии
// с материалами (/learn/[materialId]) и задачами (/issues/[issueId]).
export default async function CourseQuizPage({ params }: Props) {
  const { quizId } = await params;
  return <CourseQuizClient quizId={quizId} />;
}
