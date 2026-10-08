"use client";

import { useEffect, useRef, useState } from "react";
import type { UseFormReturn } from "react-hook-form";

export type AutosaveStatus = "idle" | "saving" | "saved" | "error";

interface UseFormAutosaveOptions<T extends Record<string, unknown>> {
  form: UseFormReturn<T>;
  /** Disabled when null/undefined — used for create-mode where no entity exists yet. */
  enabled: boolean;
  /** Saves debounced values. Called only when form is dirty since last save. */
  save: (values: T) => Promise<void>;
  /** Debounce window in ms. Default 1500. */
  debounceMs?: number;
}

/**
 * Минимальный auto-save: подписывается на form.watch, с debounce'ом вызывает save(values),
 * пока form dirty. После успешного save сбрасывает dirty-state через form.reset(values, { keepDirty: false }).
 *
 * Не делает оптимистичных reads — caller сам решает, нужно ли invalidate'ить queryClient
 * после успешного save (обычно нужно, чтобы детали других вкладок подтянулись).
 */
export function useFormAutosave<T extends Record<string, unknown>>({
  form,
  enabled,
  save,
  debounceMs = 1500,
}: UseFormAutosaveOptions<T>): AutosaveStatus {
  const [status, setStatus] = useState<AutosaveStatus>("idle");
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const inflightRef = useRef(false);

  useEffect(() => {
    if (!enabled) return undefined;

    const scheduleSave = () => {
      if (timerRef.current) clearTimeout(timerRef.current);
      timerRef.current = setTimeout(async () => {
        // Если предыдущий save ещё не завершился — пропускаем тик и
        // дожидаемся завершения. После него re-check isDirty в finally блоке
        // самого save запустит новую попытку, чтобы не потерять keystrokes,
        // напечатанные во время in-flight'а.
        if (inflightRef.current) return;
        inflightRef.current = true;
        setStatus("saving");
        const valuesToSave = form.getValues();
        try {
          await save(valuesToSave);
          form.reset(valuesToSave, { keepDirty: false, keepValues: true });
          setStatus("saved");
        } catch {
          // Ошибка surface'ится через toast в save() — здесь только статус.
          setStatus("error");
        } finally {
          inflightRef.current = false;
          // Юзер мог напечатать ещё что-то пока save был in-flight — re-check
          // dirty и schedule следующий цикл, иначе keystrokes теряются.
          if (form.formState.isDirty) {
            scheduleSave();
          }
        }
      }, debounceMs);
    };

    const subscription = form.watch((_values, { type }) => {
      // type=='change' срабатывает на user-input в любое поле; reset/setValue со скрытым
      // shouldDirty=false — не триггерит, что нам и нужно (post-save reset не зацикливает).
      if (type !== "change") return;
      if (!form.formState.isDirty) return;
      scheduleSave();
    });

    return () => {
      subscription.unsubscribe();
      if (timerRef.current) clearTimeout(timerRef.current);
    };
  }, [form, save, enabled, debounceMs]);

  return status;
}
