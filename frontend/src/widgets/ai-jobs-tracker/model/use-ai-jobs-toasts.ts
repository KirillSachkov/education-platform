"use client";

import type { ActiveAiJobDto, ActiveAiJobKind } from "@/entities/material-processing";
import { useEffect, useRef } from "react";
import { toast } from "sonner";

const KIND_GENITIVE: Record<ActiveAiJobKind, string> = {
  TRANSCRIPT: "транскрипта",
  TIMECODES: "тайм-кодов",
  CONTENT: "конспекта",
};

/**
 * Сравнивает active jobs между poll-tick'ами; на исчезновение job'а из списка
 * шлёт toast.success / toast.error в зависимости от error fields на последнем
 * snapshot'е. Без этого автор не узнаёт о завершении генерации, если ушёл со
 * страницы edit-формы материала.
 */
export function useAiJobsToasts(jobs: ActiveAiJobDto[], enabled: boolean) {
  const previousJobsRef = useRef<Map<string, ActiveAiJobDto>>(new Map());

  useEffect(() => {
    if (!enabled) {
      previousJobsRef.current = new Map();
      return;
    }

    const currentMap = new Map(jobs.map((j) => [j.jobId, j] as const));
    const previous = previousJobsRef.current;

    previous.forEach((prevJob, jobId) => {
      if (currentMap.has(jobId)) return;

      // Job исчез из active. Если errorCode/errorMessage были в последнем
      // snapshot'е — не значит «failed», там могут быть промежуточные ошибки.
      // Backend пишет error fields на FAILED → если они есть, значит закончился
      // ошибкой; если null — успешно завершился.
      const kind = KIND_GENITIVE[prevJob.jobKind];
      if (prevJob.errorCode || prevJob.errorMessage) {
        toast.error(`Ошибка генерации ${kind}`, {
          description: prevJob.errorMessage ?? undefined,
        });
      } else {
        toast.success(`Генерация ${kind} завершена`);
      }
    });

    previousJobsRef.current = currentMap;
  }, [jobs, enabled]);
}
