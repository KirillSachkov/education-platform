"use client";

import { quizQueryOptions } from "@/entities/quiz";
import { getErrorMessage } from "@/shared/api";
import { Icons } from "@/shared/ui/icons";
import { Button } from "@/shared/ui/kit/button";
import { EmptyState } from "@/shared/ui/kit/empty-state";
import { Skeleton } from "@/shared/ui/kit/skeleton";
import { useQuery } from "@tanstack/react-query";
import { useCreateLevelTest } from "../model/use-create-level-test";
import { LevelTestEditor } from "./level-test-editor";

/**
 * Страница «Тест уровня» автора (#487): loading → CTA создания (теста ещё нет) →
 * редактор самого свежего level-test'а. Key по id+updatedAt ремоунтит редактор
 * свежим серверным состоянием после сохранения (как material-quiz-builder-section).
 */
export function LevelTestManager() {
  const { data: levelTests, isLoading, error } = useQuery(quizQueryOptions.myLevelTestsOptions());
  const createMutation = useCreateLevelTest();

  let body: React.ReactNode;
  if (isLoading) {
    body = (
      <div className="space-y-4">
        <Skeleton className="h-10 w-full max-w-xl" />
        <Skeleton className="h-40 w-full" />
        <Skeleton className="h-40 w-full" />
      </div>
    );
  } else if (error) {
    body = (
      <p className="text-sm text-destructive">
        {getErrorMessage(error, "Не удалось загрузить тест уровня")}
      </p>
    );
  } else if (!levelTests || levelTests.length === 0) {
    body = (
      <EmptyState
        variant="dashed"
        icon={Icons.listChecks}
        title="Теста уровня пока нет"
        description="Создайте входной тест «Определи свой уровень» — он появится на публичной странице после публикации и будет рекомендовать ваши курсы по слабым секциям."
        action={
          <Button onClick={() => createMutation.mutate()} disabled={createMutation.isPending}>
            {createMutation.isPending ? (
              <Icons.loading className="size-4 animate-spin" />
            ) : (
              <Icons.add className="size-4" />
            )}
            Создать тест уровня
          </Button>
        }
      />
    );
  } else {
    const quiz = levelTests[0];
    body = (
      <>
        {levelTests.length > 1 && (
          <p className="mb-4 text-xs text-muted-foreground">
            Найдено тестов уровня: {levelTests.length}. Показан самый свежий — на публичной странице
            действует последний опубликованный.
          </p>
        )}
        <LevelTestEditor key={`${quiz.id}-${quiz.updatedAt}`} quiz={quiz} />
      </>
    );
  }

  return (
    <div className="mx-auto mt-8 max-w-5xl space-y-6 px-4 pb-16">
      <header className="space-y-1">
        <h1 className="text-2xl font-semibold tracking-tight">Тест уровня</h1>
        <p className="text-sm text-muted-foreground">
          Входной тест «Определи свой уровень»: вопросы по секциям, пороги Junior/Middle/Senior и
          рекомендации курсов по результату
        </p>
      </header>
      {body}
    </div>
  );
}
