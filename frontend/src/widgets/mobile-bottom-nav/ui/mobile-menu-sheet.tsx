"use client";

import { useSession } from "next-auth/react";
import Link from "next/link";
import type { ReactNode } from "react";
import { useUnreadCount } from "@/features/notifications";
import { useMyProfile } from "@/features/profile-manage";
import { fullLogout, ROLES, useIsAuthenticated, useRoles } from "@/shared/auth";
import {
  accountNav,
  adminNav,
  canViewNavItem,
  learningNav,
  teachingNav,
  type NavItem,
} from "@/shared/config/app-navigation";
import { routes } from "@/shared/config/routes";
import { cn } from "@/shared/lib/css";
import { UserAvatar } from "@/shared/ui/components";
import { Icons } from "@/shared/ui/icons";
import { Button, buttonVariants } from "@/shared/ui/kit/button";
import { Sheet, SheetClose, SheetContent, SheetHeader, SheetTitle } from "@/shared/ui/kit/sheet";
import { BOTTOM_NAV_TABS } from "../lib/active-tab";

/**
 * Шторка «Меню» нижнего таб-бара: все разделы, не уместившиеся в пять вкладок,
 * сгруппированные по ролям. Пункты берутся из `shared/config/app-navigation.ts`
 * (тот же source of truth, что у `AppSidebar` / `AppSectionTopTabs`) — секции
 * не могут разъехаться с десктопной навигацией.
 */

// Хвосты learningNav, которые уже есть вкладками в баре, в шторке не дублируем.
const TAB_LINK_HREFS = new Set(
  BOTTOM_NAV_TABS.filter((tab) => tab.href !== null).map((tab) => tab.href as string),
);

interface MobileMenuSheetProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

export function MobileMenuSheet({ open, onOpenChange }: MobileMenuSheetProps) {
  const isAuth = useIsAuthenticated();
  const { isAtLeast, hasAnyRole } = useRoles();
  const canTeach = isAtLeast(ROLES.AUTHOR);
  const canAdmin = isAtLeast(ROLES.ADMIN);

  const learningItems = learningNav.filter((item) => !TAB_LINK_HREFS.has(item.href));
  const teachingItems = teachingNav.filter((item) =>
    canViewNavItem(item, { isAtLeast, hasAnyRole }),
  );

  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent
        id="mobile-menu-sheet"
        side="right"
        // 88vw оставляет слева «подглядывание» затемнённого контента — аффорданс
        // «тапни мимо, чтобы закрыть» (как в нативных drawer'ах).
        className="w-[88vw] max-w-sm gap-0 p-0 md:hidden"
      >
        <SheetHeader className="shrink-0 border-b border-border/50 px-4 pb-3 pt-[calc(env(safe-area-inset-top)+14px)]">
          <SheetTitle className="text-base">Меню</SheetTitle>
        </SheetHeader>

        <div
          className="flex-1 space-y-6 overflow-y-auto overscroll-contain px-4 pt-4"
          style={{ paddingBottom: "calc(env(safe-area-inset-bottom) + 24px)" }}
        >
          {isAuth ? <MenuUserCard /> : <MenuLoginCard />}

          <MenuGroup title="Обучение" items={learningItems} />

          {isAuth && <MenuAccountGroup />}

          {canTeach && <MenuGroup title="Преподавание" items={teachingItems} />}

          {canAdmin && <MenuGroup title="Администрирование" items={adminNav} />}

          {isAuth && (
            <Button
              variant="outline"
              className="w-full justify-center gap-2 rounded-xl text-red border-red/30 hover:bg-red/5 hover:text-red"
              onClick={() => fullLogout()}
            >
              <Icons.logout className="size-4" />
              Выйти
            </Button>
          )}
        </div>
      </SheetContent>
    </Sheet>
  );
}

/** Карточка юзера → /profile. Рендерится только для авторизованных. */
function MenuUserCard() {
  const { data: session } = useSession();
  const { profile } = useMyProfile();
  const name = profile?.displayName || profile?.username || session?.user?.name || "Пользователь";
  const email = session?.user?.email;

  return (
    <SheetClose asChild>
      <Link
        href={routes.profile}
        className={cn(
          "flex min-h-[44px] items-center gap-3 rounded-2xl border border-border/50 bg-card/40 p-3",
          "transition-colors active:bg-secondary/60",
        )}
      >
        <UserAvatar
          name={name}
          avatarId={profile?.avatarId}
          className="size-11 ring-1 ring-primary/20"
        />
        <span className="min-w-0 flex-1">
          <span className="block truncate text-sm font-semibold">{name}</span>
          {email && <span className="block truncate text-xs text-muted-foreground">{email}</span>}
        </span>
        <Icons.chevronRight className="size-4 shrink-0 text-muted-foreground/60" />
      </Link>
    </SheetClose>
  );
}

function MenuLoginCard() {
  return (
    <SheetClose asChild>
      <Link
        href={routes.login}
        className={cn(buttonVariants(), "w-full justify-center gap-2 rounded-xl")}
      >
        <Icons.login className="size-4" />
        Войти
      </Link>
    </SheetClose>
  );
}

/** «Аккаунт» вынесен в компонент ради бейджа непрочитанных на «Уведомлениях». */
function MenuAccountGroup() {
  const { count } = useUnreadCount();

  return (
    <MenuGroupShell title="Аккаунт">
      {accountNav.map((item) => (
        <li key={item.href}>
          <MenuRow
            item={item}
            trailing={
              item.href === routes.notifications && count > 0 ? (
                <span className="flex h-5 min-w-5 shrink-0 items-center justify-center rounded-full bg-primary px-1.5 text-[11px] font-semibold text-primary-foreground">
                  {count > 99 ? "99+" : count}
                </span>
              ) : undefined
            }
          />
        </li>
      ))}
    </MenuGroupShell>
  );
}

function MenuGroup({ title, items }: { title: string; items: NavItem[] }) {
  if (items.length === 0) return null;
  return (
    <MenuGroupShell title={title}>
      {items.map((item) => (
        <li key={item.href}>
          <MenuRow item={item} />
        </li>
      ))}
    </MenuGroupShell>
  );
}

function MenuGroupShell({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section>
      <h2 className="mb-2 px-1 text-xs font-semibold uppercase tracking-wider text-muted-foreground/80">
        {title}
      </h2>
      <ul className="divide-y divide-border/40 overflow-hidden rounded-2xl border border-border/50 bg-card/40">
        {children}
      </ul>
    </section>
  );
}

function MenuRow({ item, trailing }: { item: NavItem; trailing?: ReactNode }) {
  const Icon = item.icon;
  return (
    <SheetClose asChild>
      <Link
        href={item.href}
        className={cn(
          "flex items-center gap-3 px-4 py-3",
          "transition-colors active:bg-secondary/60",
          "min-h-[44px]",
        )}
      >
        <span className="flex size-9 shrink-0 items-center justify-center rounded-xl bg-secondary/50 text-foreground">
          <Icon className="size-[18px]" />
        </span>
        <span className="flex-1 truncate text-sm font-medium">{item.label}</span>
        {trailing}
        <Icons.chevronRight className="size-4 shrink-0 text-muted-foreground/60" />
      </Link>
    </SheetClose>
  );
}
