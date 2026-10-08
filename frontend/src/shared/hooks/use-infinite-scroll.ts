"use client";

import { useEffect, useRef } from "react";

type UseInfiniteScrollOptions = {
  hasNextPage: boolean | undefined;
  isFetchingNextPage: boolean;
  fetchNextPage: () => void;
  threshold?: number;
};

/**
 * Подгрузка следующей страницы, когда sentinel-элемент попадает во вьюпорт.
 *
 * КОНТРАКТ: `setCursorRef` вешается на пустой sentinel-`<div>`, который ОБЯЗАН
 * находиться ВНУТРИ скроллящегося контейнера (`overflow-y-auto`) сразу после списка.
 * Если sentinel лежит снаружи (в общем потоке страницы), он всегда виден во вьюпорте
 * при высоте контейнера меньше контента → observer срабатывает на первой отрисовке и
 * сливает все страницы подряд. Референс корректного размещения — UserSearchCombobox.
 */
export function useInfiniteScroll({
  hasNextPage,
  isFetchingNextPage,
  fetchNextPage,
  threshold = 0.5,
}: UseInfiniteScrollOptions) {
  const cursorRef = useRef<HTMLDivElement | null>(null);

  useEffect(() => {
    const el = cursorRef.current;
    if (!el || !hasNextPage || isFetchingNextPage) return;

    const observer = new IntersectionObserver(
      (entries) => {
        if (entries[0].isIntersecting) {
          fetchNextPage();
        }
      },
      { threshold },
    );

    observer.observe(el);

    return () => observer.disconnect();
  }, [hasNextPage, isFetchingNextPage, fetchNextPage, threshold]);

  const setCursorRef = (el: HTMLDivElement | null) => {
    cursorRef.current = el;
  };

  return setCursorRef;
}
