"use client";

import Link from "next/link";
import { useSelectedLayoutSegment } from "next/navigation";
import { type IconComponent, Icons } from "@/shared/ui/icons";
import { NavLinkPending } from "@/shared/ui/components";
import { cn } from "@/shared/lib/css";
import { routes } from "@/shared/config/routes";
import { Button } from "@/shared/ui/kit/button";

type SettingsItem = {
  segment: string;
  href: string;
  label: string;
  description: string;
  icon: IconComponent;
};

const ITEMS: SettingsItem[] = [
  {
    segment: "account",
    href: routes.settingsAccount,
    label: "Аккаунт",
    description: "Имя, email, удаление",
    icon: Icons.user,
  },
  {
    segment: "security",
    href: routes.settingsSecurity,
    label: "Безопасность",
    description: "Сессии, пароль",
    icon: Icons.shield,
  },
  {
    segment: "integrations",
    href: routes.settingsIntegrations,
    label: "Интеграции",
    description: "GitHub, Telegram, чаты курсов",
    icon: Icons.attachment,
  },
  {
    segment: "notifications",
    href: routes.settingsNotifications,
    label: "Уведомления",
    description: "Каналы и типы",
    icon: Icons.notification,
  },
  {
    segment: "subscriptions",
    href: routes.settingsSubscriptions,
    label: "Подписки",
    description: "Курсы и авторы",
    icon: Icons.users,
  },
  {
    segment: "appearance",
    href: routes.settingsAppearance,
    label: "Внешний вид",
    description: "Тема: светлая / тёмная / системная",
    icon: Icons.themeDark,
  },
];

/**
 * Hub-layout для /settings/*. Внутренний vertical sidebar слева + contentArea справа.
 *
 * Mobile-паттерн (iOS-style drill-in):
 *  - `/settings`            → видна только sidebar-list (children = пустой placeholder).
 *  - `/settings/{section}`  → виден только content (с back-кнопкой); sidebar спрятан.
 * Desktop — обе колонки одновременно, без back-кнопки.
 */
export function SettingsShell({ children }: { children: React.ReactNode }) {
  const segment = useSelectedLayoutSegment();
  const active = ITEMS.find((i) => i.segment === segment) ?? null;

  return (
    <div className="mx-auto w-full max-w-5xl px-4 sm:px-6 py-6 sm:py-10">
      {/* Page header — скрыт на мобиле в detail-режиме, чтобы не дублировать заголовок секции */}
      <header className={cn("mb-6 sm:mb-8 space-y-1.5", active && "hidden md:block")}>
        <h1 className="text-2xl sm:text-3xl font-semibold tracking-tight">Настройки</h1>
        <p className="text-sm text-muted-foreground">
          Управление аккаунтом, уведомлениями и связанными сервисами
        </p>
      </header>

      <div className="flex flex-col md:flex-row gap-6 md:gap-10">
        {/* Sidebar */}
        <aside className={cn("md:w-60 lg:w-64 shrink-0", active ? "hidden md:block" : "block")}>
          <nav className="flex flex-col gap-0.5">
            {ITEMS.map((item) => {
              const isActive = item.segment === segment;
              const Icon = item.icon;
              return (
                <Link
                  key={item.segment}
                  href={item.href}
                  className={cn(
                    "group flex items-center gap-3 rounded-xl px-3 py-2.5 transition-colors",
                    "border border-transparent",
                    isActive
                      ? "bg-primary/10 border-primary/20 text-foreground"
                      : "text-muted-foreground hover:text-foreground hover:bg-muted/40",
                  )}
                >
                  <span
                    className={cn(
                      "flex size-9 shrink-0 items-center justify-center rounded-lg transition-colors",
                      isActive
                        ? "bg-primary/15 text-primary"
                        : "bg-muted/60 text-muted-foreground group-hover:text-foreground",
                    )}
                  >
                    <Icon className="size-4" />
                  </span>
                  <span className="min-w-0 flex-1">
                    <span className="block text-sm font-medium leading-snug">{item.label}</span>
                    <span className="block text-xs text-muted-foreground/80 leading-snug truncate">
                      {item.description}
                    </span>
                  </span>
                  <NavLinkPending className="ml-0 mr-1" />
                  <Icons.chevronRight
                    className={cn(
                      "size-4 shrink-0 transition-opacity",
                      isActive ? "opacity-60" : "opacity-0 group-hover:opacity-40",
                      "md:hidden",
                    )}
                  />
                </Link>
              );
            })}
          </nav>
        </aside>

        {/* Content */}
        <main className={cn("flex-1 min-w-0", active ? "block" : "hidden md:block")}>
          {/* Mobile back-link — родительская страница `/settings` рендерит список секций. */}
          {active && (
            <div className="md:hidden mb-4 flex items-center gap-2">
              <Button asChild variant="ghost" size="sm" className="-ml-2 text-muted-foreground">
                <Link href={routes.settings}>
                  <Icons.chevronLeft className="size-4" />
                  Настройки
                </Link>
              </Button>
            </div>
          )}

          {/* Section heading on desktop */}
          {active && (
            <div className="hidden md:block mb-6 space-y-1">
              <h2 className="text-xl font-semibold tracking-tight">{active.label}</h2>
              <p className="text-sm text-muted-foreground">{active.description}</p>
            </div>
          )}

          {/* Section heading on mobile (own page header inside detail) */}
          {active && (
            <div className="md:hidden mb-5 space-y-1">
              <h2 className="text-2xl font-semibold tracking-tight">{active.label}</h2>
              <p className="text-sm text-muted-foreground">{active.description}</p>
            </div>
          )}

          {children}
        </main>
      </div>
    </div>
  );
}
