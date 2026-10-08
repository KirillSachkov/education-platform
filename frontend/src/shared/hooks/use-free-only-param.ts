"use client";

import { usePathname, useRouter, useSearchParams } from "next/navigation";

/**
 * Хук для toggle «Только бесплатное» — двунаправленная синхронизация с URL `?free=1`.
 * Состояние в URL чтобы было shareable + survived hard reload.
 *
 * Возвращает кортеж: `[active, setActive]` (привычно как у `useState`).
 *
 * Использует `router.replace`, не `push` — toggle filter'а не должен засорять history-stack.
 */
export function useFreeOnlyParam(): [boolean, (next: boolean) => void] {
  const router = useRouter();
  const pathname = usePathname();
  const searchParams = useSearchParams();

  const active = searchParams.get("free") === "1";

  const setActive = (next: boolean) => {
    const params = new URLSearchParams(searchParams.toString());
    if (next) {
      params.set("free", "1");
    } else {
      params.delete("free");
    }
    const queryString = params.toString();
    router.replace(queryString ? `${pathname}?${queryString}` : pathname, { scroll: false });
  };

  return [active, setActive];
}
