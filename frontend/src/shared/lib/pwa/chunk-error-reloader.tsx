"use client";

import { useEffect } from "react";

/**
 * После деплоя меняются хеши чанков (`/_next/static/chunks/<hash>.js`). Вкладка,
 * открытая до деплоя, ссылается на старые хеши — при навигации они уже 404 и Next
 * бросает `ChunkLoadError` («Failed to load chunk …»). Лечится hard-reload'ом: свежий
 * HTML тянет актуальные хеши. Перезагружаемся ровно один раз за сессию (sessionStorage
 * guard), чтобы не словить бесконечный цикл, если reload не помог.
 */
const RELOAD_FLAG = "chunk-reload-attempted";

function isChunkLoadError(value: unknown): boolean {
  const text =
    value instanceof Error ? `${value.name} ${value.message}` : String(value ?? "");
  return /ChunkLoadError|Loading chunk [\w-]+ failed|Failed to load chunk|error loading dynamically imported module/i.test(
    text,
  );
}

export function ChunkErrorReloader() {
  useEffect(() => {
    // Мы исполняемся → основной бандл загрузился успешно. Сбрасываем one-shot guard,
    // чтобы будущая stale-chunk ошибка (после следующего деплоя) тоже могла само-зажиться.
    try {
      sessionStorage.removeItem(RELOAD_FLAG);
    } catch {
      // sessionStorage недоступен (приватный режим и т.п.) — не критично
    }

    const reloadOnce = () => {
      try {
        if (sessionStorage.getItem(RELOAD_FLAG)) return;
        sessionStorage.setItem(RELOAD_FLAG, "1");
      } catch {
        // нет sessionStorage — перезагружаемся без loop-guard (best effort)
      }
      window.location.reload();
    };

    const onError = (event: ErrorEvent) => {
      if (isChunkLoadError(event.error ?? event.message)) reloadOnce();
    };
    const onRejection = (event: PromiseRejectionEvent) => {
      if (isChunkLoadError(event.reason)) reloadOnce();
    };

    window.addEventListener("error", onError);
    window.addEventListener("unhandledrejection", onRejection);
    return () => {
      window.removeEventListener("error", onError);
      window.removeEventListener("unhandledrejection", onRejection);
    };
  }, []);

  return null;
}
