"use client";

import { getErrorMessage } from "@/shared/api/errors";

export default function CourseError({ error, reset }: { error: Error; reset: () => void }) {
  return (
    <div className="flex flex-col items-center justify-center h-full gap-4 p-6">
      <h2 className="text-lg font-semibold">Произошла ошибка</h2>
      <p className="text-muted-foreground text-center">
        {getErrorMessage(error, "Произошла ошибка")}
      </p>
      <button onClick={reset} className="text-primary underline text-sm">
        Попробовать снова
      </button>
    </div>
  );
}
