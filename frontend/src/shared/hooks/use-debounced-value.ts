import { useEffect, useState } from "react";

/**
 * Возвращает значение, обновляющееся с задержкой после последнего изменения `value`.
 * Полезно для поиска/фильтров, чтобы не дергать API на каждую клавишу.
 */
export function useDebouncedValue<T>(value: T, delayMs: number): T {
  const [debounced, setDebounced] = useState(value);

  useEffect(() => {
    const id = setTimeout(() => setDebounced(value), delayMs);
    return () => clearTimeout(id);
  }, [value, delayMs]);

  return debounced;
}
