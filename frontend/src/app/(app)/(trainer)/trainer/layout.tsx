import { AppLayout } from "@/widgets/layouts/app-layout";
import { TrainerSidebar } from "@/widgets/sidebar";
import { readSidebarDefaultOpen } from "@/shared/lib/sidebar-server";
import { Suspense } from "react";

/**
 * Раздел «Тренажёр» — отдельная часть платформы со своим сайдбаром (#623), по той же
 * модели, что курс с `CourseSidebar` (`(course)`-группа). Route-группа `(trainer)` не
 * меняет URL — `/trainer/*` остаётся прежним, меняется только подставляемый сайдбар.
 * `TrainerSidebar` читает `?tab=` (useSearchParams) → оборачиваем в Suspense.
 */
export default async function TrainerSectionLayout({
  children,
}: Readonly<{ children: React.ReactNode }>) {
  const defaultSidebarOpen = await readSidebarDefaultOpen();
  return (
    <AppLayout
      accent="trainer"
      defaultSidebarOpen={defaultSidebarOpen}
      sidebar={
        <Suspense fallback={null}>
          <TrainerSidebar />
        </Suspense>
      }
    >
      {children}
    </AppLayout>
  );
}
