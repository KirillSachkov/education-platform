import type { Metadata } from "next";
import { LevelTestResultView } from "@/features/level-test-runner";

// Персональный результат — не индексируем; остальное SEO воронки — ST-7.
export const metadata: Metadata = {
  title: "Результат теста уровня — SachkovLearn",
  description: "Твой уровень .NET-разработчика: общий процент, разбор по темам и рекомендация курса.",
  robots: { index: false },
};

interface Props {
  params: Promise<{ attemptId: string }>;
}

/**
 * `/level-test/result/[attemptId]` — страница результата (#481). Аноним видит
 * lead-gated тизер с CTA на логин; после возврата с авторизации попытка
 * клеймится и открывается полный разбор (включая AI-оценку открытых ответов).
 */
export default async function LevelTestResultPage({ params }: Props) {
  const { attemptId } = await params;
  return (
    <div className="px-4">
      <LevelTestResultView attemptId={attemptId} />
    </div>
  );
}
