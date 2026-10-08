"use client";

import { useQuery } from "@tanstack/react-query";
import { courseLearningStateQueryOptions, isCertificateEligible } from "@/entities/course-progress";
import { courseCurriculumQueryOptions } from "@/entities/course";
import { ClaimCertificateButton } from "@/features/claim-certificate";
import { useCourseId } from "@/shared/providers/course-id-provider";
import { formatRuPlural, RU_PLURALS } from "@/shared/lib/pluralize";
import { Icons } from "@/shared/ui/icons";

export default function ProgressPage() {
  const courseId = useCourseId();
  const { data: learningState } = useQuery(courseLearningStateQueryOptions(courseId));
  const { data: curriculum } = useQuery(courseCurriculumQueryOptions(courseId));

  const summary = learningState?.summary;

  // progressPercent приходит с бэкенда — floor(completedItems / totalItems * 100), включает
  // материалы + задания + тесты. Единый источник и для отображения, и для гейта сертификата.
  const progressPercent = summary?.progressPercent ?? 0;

  // Сертификат доступен при ≥80% всех элементов программы (#650). isCertificateEligible
  // зеркалит backend-формулу claim-эндпоинта — кнопка и сервер не расходятся.
  const isCertificateAvailable = !!summary && isCertificateEligible(summary);

  return (
    <div className="p-6 max-w-4xl mx-auto">
      <h1 className="text-xl font-semibold mb-6">Прогресс</h1>

      {summary ? (
        <div className="space-y-6">
          <div className="grid grid-cols-2 sm:grid-cols-4 gap-4">
            <StatCard
              label="Материалов просмотрено"
              value={`${summary.materialsViewed}/${summary.materialsTotal}`}
            />
            <StatCard
              label="Задач выполнено"
              value={`${summary.issuesCompleted}/${summary.issuesTotal}`}
            />
            <StatCard label="Общий прогресс" value={`${progressPercent}%`} />
          </div>

          {isCertificateAvailable && (
            <div className="flex flex-col items-start justify-between gap-3 rounded-xl border border-primary/30 bg-primary/5 p-4 sm:flex-row sm:items-center">
              <div className="flex items-start gap-3">
                <div className="flex size-10 shrink-0 items-center justify-center rounded-full border border-primary/20 bg-primary/10">
                  <Icons.graduation className="size-5 text-primary" aria-hidden />
                </div>
                <div>
                  <div className="font-medium">Сертификат доступен</div>
                  <div className="text-sm text-muted-foreground mt-1">
                    Вы прошли {progressPercent}% курса — получите именной сертификат с публичной
                    ссылкой для проверки
                  </div>
                </div>
              </div>
              <ClaimCertificateButton courseId={courseId} className="shrink-0" />
            </div>
          )}

          <div>
            <h2 className="text-lg font-semibold mb-3">По модулям</h2>
            <div className="space-y-2">
              {curriculum?.sections.map((section) => {
                const totalItems = section.items.length;
                return (
                  <div key={section.id} className="p-4 border rounded-xl">
                    <div className="font-medium">{section.title}</div>
                    <div className="text-sm text-muted-foreground mt-1">
                      {formatRuPlural(totalItems, RU_PLURALS.element)}
                    </div>
                  </div>
                );
              })}
            </div>
          </div>
        </div>
      ) : (
        <p className="text-muted-foreground">Загрузка данных прогресса...</p>
      )}
    </div>
  );
}

function StatCard({ label, value }: { label: string; value: string }) {
  return (
    <div className="p-4 border rounded-xl">
      <div className="text-2xl font-bold tabular-nums">{value}</div>
      <div className="text-sm text-muted-foreground mt-1">{label}</div>
    </div>
  );
}
