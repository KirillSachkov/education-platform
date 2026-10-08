import type { Metadata } from "next";
import { LevelTestManager } from "@/features/quiz-builder";

export const metadata: Metadata = {
  title: "Тест уровня",
};

export default function AuthorLevelTestPage() {
  return <LevelTestManager />;
}
