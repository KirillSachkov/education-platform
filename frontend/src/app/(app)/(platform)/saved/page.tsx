import type { Metadata } from "next";
import { BookmarksList } from "@/features/bookmarks-list";

export const metadata: Metadata = {
  title: "Сохранённое",
  description:
    "Закладки со всех курсов — материалы и задачи, сохранённые, чтобы вернуться к ним позже.",
};

/**
 * Глобальная страница «Сохранённое» — закладки пользователя со всех курсов
 * одним списком. Использует тот же `BookmarksList`, что и course-scoped
 * страница `/courses/[courseSlug]/bookmarks`, но без `courseId` — backend
 * возвращает закладки всех курсов, карточка показывает название курса.
 * Аноним видит login-CTA внутри самого списка.
 */
export default function SavedPage() {
  return (
    <div className="mx-auto flex w-full max-w-3xl flex-col gap-6 p-4 md:p-6">
      <header className="space-y-2">
        <h1 className="text-2xl font-bold tracking-tight sm:text-3xl">Сохранённое</h1>
        <p className="text-sm text-muted-foreground sm:text-base">
          Закладки со всех ваших курсов — материалы и задачи в одном списке.
        </p>
      </header>
      <BookmarksList />
    </div>
  );
}
