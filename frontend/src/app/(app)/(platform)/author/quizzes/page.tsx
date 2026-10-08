import type { Metadata } from "next";
import { QuizLibraryManager } from "@/features/quiz-builder";

export const metadata: Metadata = {
  title: "Тесты",
};

interface Props {
  /** `?quiz=<id>` — авто-раскрыть редактор квиза (deep-link из привязок). */
  searchParams: Promise<{ quiz?: string }>;
}

export default async function AuthorQuizzesPage({ searchParams }: Props) {
  const { quiz } = await searchParams;
  return <QuizLibraryManager initialQuizId={quiz} />;
}
