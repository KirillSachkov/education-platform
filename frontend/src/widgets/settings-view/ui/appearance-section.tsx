"use client";

import { useTheme } from "next-themes";
import { useSyncExternalStore } from "react";
import { Icons } from "@/shared/ui/icons";
import { cn } from "@/shared/lib/css";

type ThemeOption = {
  value: "light" | "dark" | "system";
  label: string;
  hint: string;
  icon: keyof typeof THEME_ICONS;
};

const THEME_ICONS = {
  light: Icons.themeLight,
  dark: Icons.themeDark,
  system: Icons.themeSystem,
} as const;

const OPTIONS: ThemeOption[] = [
  {
    value: "light",
    label: "Светлая",
    hint: "Дневной режим — яркий фон",
    icon: "light",
  },
  {
    value: "dark",
    label: "Тёмная",
    hint: "«Лунная ночь» — мягкий контраст",
    icon: "dark",
  },
  {
    value: "system",
    label: "Системная",
    hint: "Следовать настройкам устройства",
    icon: "system",
  },
];

/**
 * Theme picker — three radio-style cards (light / dark / system). Persists via
 * next-themes (localStorage `theme` key + html class swap). SSR hydration guard
 * via useSyncExternalStore — selected state appears only after mount.
 */
export function AppearanceSection() {
  const { theme, setTheme } = useTheme();
  const mounted = useSyncExternalStore(
    () => () => {},
    () => true,
    () => false,
  );
  const current = mounted ? (theme ?? "system") : null;

  return (
    <section className="space-y-6">
      <header className="space-y-1.5">
        <h2 className="text-xl font-semibold tracking-tight">Внешний вид</h2>
        <p className="text-sm text-muted-foreground">
          Тема применяется немедленно и сохраняется на этом устройстве. Быстрый переключатель темы
          также есть в шапке рядом с уведомлениями.
        </p>
      </header>

      <div
        role="radiogroup"
        aria-label="Тема оформления"
        className="grid gap-3 sm:grid-cols-3"
      >
        {OPTIONS.map((option) => {
          const Icon = THEME_ICONS[option.icon];
          const active = current === option.value;
          return (
            <button
              key={option.value}
              type="button"
              role="radio"
              aria-checked={active}
              onClick={() => setTheme(option.value)}
              className={cn(
                "flex flex-col items-start gap-2 rounded-2xl border p-4 text-left transition-colors",
                "min-h-[88px] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring",
                active
                  ? "border-primary bg-primary/5"
                  : "border-border/60 hover:border-border bg-card",
              )}
            >
              <span
                className={cn(
                  "flex size-9 items-center justify-center rounded-xl",
                  active ? "bg-primary/15 text-primary" : "bg-muted text-muted-foreground",
                )}
              >
                <Icon className="size-4" />
              </span>
              <span className="flex flex-col">
                <span className="text-sm font-semibold">{option.label}</span>
                <span className="text-xs text-muted-foreground">{option.hint}</span>
              </span>
            </button>
          );
        })}
      </div>

      {!mounted && (
        <p className="text-xs text-muted-foreground" aria-hidden="true">
          Загрузка…
        </p>
      )}
    </section>
  );
}
