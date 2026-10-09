"use client";

import { fullLogout, ROLES, useRoles } from "@/shared/auth";
import { routes } from "@/shared/config/routes";
import { cn } from "@/shared/lib/css";
import { Icons, type IconComponent } from "@/shared/ui/icons";
import { Button } from "@/shared/ui/kit/button";
import Link from "next/link";

type MenuItem = {
  href: string;
  icon: IconComponent;
  label: string;
};

type MenuSection = {
  title: string;
  items: MenuItem[];
};

/**
 * Mobile-only secondary navigation list rendered on /profile. Covers every
 * destination that lives in the desktop sidebar / header user-dropdown so that
 * mobile users can reach Settings, Teaching, Admin from a single
 * tab. Desktop hides this — desktop has the sidebar instead.
 */
export function MobileAccountMenu() {
  const { isAtLeast, hasRole } = useRoles();
  const canTeach = isAtLeast(ROLES.AUTHOR);
  const canReview = hasRole(ROLES.MODERATOR) || isAtLeast(ROLES.ADMIN);
  const canAdmin = isAtLeast(ROLES.ADMIN);

  const sections: MenuSection[] = [
    {
      title: "Аккаунт",
      items: [
        { href: routes.settings, icon: Icons.settings, label: "Настройки" },
        { href: routes.pricing, icon: Icons.crown, label: "Планы доступа" },
        { href: routes.myPlans, icon: Icons.crown, label: "Мои планы" },
        { href: routes.payments, icon: Icons.creditCard, label: "Платежи" },
        { href: routes.settingsIntegrations, icon: Icons.github, label: "Интеграции" },
      ],
    },
  ];

  if (canTeach) {
    sections.push({
      title: "Преподавание",
      items: [
        { href: routes.authorCourses, icon: Icons.editAlt, label: "Курсы" },
        { href: routes.authorKnowledgeBase, icon: Icons.document, label: "Материалы" },
        { href: routes.authorCollections, icon: Icons.grid, label: "Подборки" },
        { href: routes.authorComments, icon: Icons.message, label: "Комментарии" },
      ],
    });
  }

  if (canReview) {
    sections.push({
      title: "Проверка",
      items: [{ href: routes.authorReview, icon: Icons.clipboardCheck, label: "Проверка работ" }],
    });
  }

  if (canAdmin) {
    sections.push({
      title: "Администрирование",
      items: [
        { href: routes.adminPlans, icon: Icons.crown, label: "Планы доступа" },
        { href: routes.adminUsers, icon: Icons.userSettings, label: "Пользователи" },
        { href: routes.adminAiUsage, icon: Icons.ai, label: "AI usage" },
        { href: routes.adminAiModels, icon: Icons.settings, label: "AI модели" },
      ],
    });
  }

  return (
    <div className="md:hidden mx-auto w-full max-w-3xl px-4 pb-8 space-y-6">
      {sections.map((section) => (
        <MenuGroup key={section.title} section={section} />
      ))}

      <Button
        variant="outline"
        className="w-full justify-center gap-2 rounded-xl text-red border-red/30 hover:bg-red/5 hover:text-red"
        onClick={() => fullLogout()}
      >
        <Icons.logout className="size-4" />
        Выйти
      </Button>
    </div>
  );
}

function MenuGroup({ section }: { section: MenuSection }) {
  return (
    <section>
      <h2 className="px-1 mb-2 text-xs font-semibold uppercase tracking-wider text-muted-foreground/80">
        {section.title}
      </h2>
      <ul className="overflow-hidden rounded-2xl border border-border/50 bg-card/40 divide-y divide-border/40">
        {section.items.map((item) => (
          <li key={item.href}>
            <MenuRow item={item} />
          </li>
        ))}
      </ul>
    </section>
  );
}

function MenuRow({ item }: { item: MenuItem }) {
  const Icon = item.icon;
  return (
    <Link
      href={item.href}
      // Default Next.js prefetch — settings/plans subpages are typical
      // next-taps after profile hub, prefetching makes the drill-in feel
      // instant on mobile.
      className={cn(
        "flex items-center gap-3 px-4 py-3.5",
        "transition-colors active:bg-secondary/60",
        "min-h-[44px]",
      )}
    >
      <span className="flex size-9 shrink-0 items-center justify-center rounded-xl bg-secondary/50 text-foreground">
        <Icon className="size-[18px]" />
      </span>
      <span className="flex-1 text-sm font-medium">{item.label}</span>
      <Icons.chevronRight className="size-4 text-muted-foreground/60" />
    </Link>
  );
}
