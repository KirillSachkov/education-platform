"use client";

import Link from "next/link";
import { ArrowLeft, BookOpen } from "lucide-react";

/**
 * Локальный 404 для платформенных страниц. Живёт ВНУТРИ (app)/(platform), поэтому
 * наследует SessionProvider/QueryClientProvider/SessionGuard — после `Назад` юзер
 * не теряет авторизацию и кэш query-клиента не сбрасывается. Глобальный
 * `app/not-found.tsx` остаётся для public-роутов вне (app).
 */
export default function PlatformNotFound() {
  return (
    <div className="flex flex-1 items-center justify-center px-6 py-20">
      <div className="max-w-lg w-full text-center space-y-8">
        <div className="relative inline-block">
          <span className="font-[family-name:var(--font-jetbrains-mono)] text-[8rem] sm:text-[10rem] font-extrabold leading-none tracking-tighter bg-gradient-to-br from-primary via-cyan to-primary bg-clip-text text-transparent select-none">
            404
          </span>
        </div>
        <p className="text-muted-foreground text-base leading-relaxed">
          Страница не найдена. Возможно, она была удалена или перемещена.
        </p>
        <div className="flex flex-wrap items-center justify-center gap-4">
          <Link
            href="/"
            className="inline-flex items-center gap-2 rounded-xl border border-border px-6 py-3 text-sm font-semibold text-foreground hover:bg-secondary transition-colors"
          >
            <ArrowLeft className="size-4" />
            На главную
          </Link>
          <Link
            href="/"
            className="inline-flex items-center gap-2 rounded-xl bg-gradient-primary px-6 py-3 text-sm font-bold text-primary-foreground shadow-lg shadow-primary/20 hover:shadow-xl hover:shadow-primary/25 hover:-translate-y-0.5 transition-all"
          >
            <BookOpen className="size-4" />
            На главную
          </Link>
        </div>
      </div>
    </div>
  );
}
