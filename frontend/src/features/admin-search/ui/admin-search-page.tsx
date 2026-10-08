"use client";

import { Icons } from "@/shared/ui/icons";
import { Button } from "@/shared/ui/kit/button";
import { useTriggerReindex } from "../model/use-trigger-reindex";

export function AdminSearchPage() {
  const reindex = useTriggerReindex();

  return (
    <div className="mx-auto max-w-3xl space-y-6 p-6">
      <header className="space-y-1.5">
        <h1 className="text-2xl font-semibold text-foreground">
          Полнотекстовый поиск
        </h1>
        <p className="text-sm text-muted-foreground">
          Управление индексом Typesense — пересчёт и диагностика.
        </p>
      </header>

      <section className="rounded-2xl border border-border/60 bg-card/40 p-5 shadow-sm">
        <div className="flex items-start gap-3">
          <div className="mt-0.5 inline-flex size-10 shrink-0 items-center justify-center rounded-xl bg-primary/10 text-primary">
            <Icons.refresh size={18} />
          </div>
          <div className="min-w-0 flex-1 space-y-3">
            <div className="space-y-1">
              <h2 className="text-base font-semibold text-foreground">
                Полный reindex
              </h2>
              <p className="text-sm leading-6 text-muted-foreground">
                Пересчитывает индекс поиска по всем опубликованным сущностям
                (курсы, модули, проекты, материалы, задания). Идёт пачками по
                200 документов с паузой 100 ms между батчами — не должен
                перегрузить БД даже при большом объёме. По завершении alias
                атомарно переключается на новую коллекцию.
              </p>
            </div>

            <details className="rounded-lg border border-border/40 bg-background/40 p-3 text-sm text-muted-foreground">
              <summary className="cursor-pointer font-medium text-foreground">
                Когда это нужно
              </summary>
              <ul className="mt-2 list-disc space-y-1 pl-5 leading-6">
                <li>
                  <strong>Первичное заполнение</strong> — после первой раскатки
                  SearchService на пустой индекс.
                </li>
                <li>
                  <strong>Схема индекса изменилась</strong> — добавилось новое
                  поле (например, <code>author_id</code>) — старые документы
                  без него нужно перестроить.
                </li>
                <li>
                  <strong>Disaster recovery</strong> — потеря Typesense volume
                  или обнаружение дрейфа между БД и индексом.
                </li>
                <li>
                  <strong>НЕ нужно</strong> для обычных правок контента —
                  runtime handlers обновляют индекс сами через события
                  (<code>material.created</code>, <code>published</code> и т.д.).
                </li>
              </ul>
            </details>

            <div className="flex flex-wrap items-center gap-3">
              <Button
                type="button"
                onClick={() => reindex.mutate()}
                disabled={reindex.isPending}
              >
                {reindex.isPending ? (
                  <>
                    <Icons.loading className="size-4 animate-spin" />
                    Запускается…
                  </>
                ) : (
                  <>
                    <Icons.refresh size={16} />
                    Запустить reindex
                  </>
                )}
              </Button>
              {reindex.data && (
                <span className="text-xs text-muted-foreground">
                  Последний запуск:{" "}
                  <code className="rounded bg-muted px-1.5 py-0.5">
                    {reindex.data.requestId.slice(0, 8)}…
                  </code>{" "}
                  · {new Date(reindex.data.requestedAtUtc).toLocaleTimeString("ru")}
                </span>
              )}
            </div>

            <p className="text-xs leading-5 text-muted-foreground">
              Операция асинхронная: сервер сразу возвращает{" "}
              <code>requestId</code> и выполняет реиндекс в фоне. Прогресс видно
              в логах <code>search-service</code> (ищи{" "}
              <code>ProcessedDocuments</code>). При успехе alias переключится
              автоматически — downtime поиска не будет.
            </p>
          </div>
        </div>
      </section>
    </div>
  );
}
