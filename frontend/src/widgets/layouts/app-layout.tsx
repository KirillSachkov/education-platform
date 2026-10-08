"use client";

import { LevelUpCelebration } from "@/features/level-up-celebration";
import { NotificationBell } from "@/features/notifications";
import { useMyProfile } from "@/features/profile-manage";
import { TelegramLinkPromptRow } from "@/features/telegram-link";
import { fullLogout, ROLES, useRoles } from "@/shared/auth";
import { routes } from "@/shared/config/routes";
import { cn } from "@/shared/lib/css";
import { useCourseContext } from "@/shared/providers/course-id-provider";
import { SeasonalHeaderDecor, UserAvatar } from "@/shared/ui/components";
import { Icons } from "@/shared/ui/icons";
import { Button } from "@/shared/ui/kit/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/shared/ui/kit/dropdown-menu";
import { SidebarInset, SidebarProvider } from "@/shared/ui/kit/sidebar";
import { Skeleton } from "@/shared/ui/kit/skeleton";
import { ThemeToggle } from "@/shared/ui/kit/theme-toggle";
import { MobileBottomNav, MobileTabTransition } from "@/widgets/mobile-bottom-nav";
import { PwaInstallBanner } from "@/widgets/pwa-install-banner";
import { AppSectionTopTabs } from "@/widgets/section-top-tabs";
import { AppSidebar } from "@/widgets/sidebar/app-sidebar";
import {
  ChevronDown,
  LogIn,
  LogOut,
  Presentation,
  Settings,
  ShieldCheck,
  User,
} from "lucide-react";
import { useSession } from "next-auth/react";
import dynamic from "next/dynamic";
import Link from "next/link";
import { usePathname } from "next/navigation";

const GlobalSearch = dynamic(() =>
  import("@/features/global-search").then((module) => module.GlobalSearch),
);

export function AppLayout({
  children,
  sidebar,
  accent,
  defaultSidebarOpen = true,
}: Readonly<{
  children: React.ReactNode;
  sidebar?: React.ReactNode;
  /** `"trainer"` тонирует весь shell в фиолетовую тему раздела «Тренажёр» (#623). */
  accent?: "trainer";
  /**
   * Persisted sidebar open/collapsed state, read server-side from the cookie
   * (`readSidebarDefaultOpen`). Threaded through so the choice survives
   * navigation between route-group layouts (each mounts its own provider).
   */
  defaultSidebarOpen?: boolean;
}>) {
  const { data: session, status } = useSession();
  const { profile } = useMyProfile();
  const { isAtLeast } = useRoles();
  const isLoading = status === "loading";
  const user = session?.user;
  const canTeach = isAtLeast(ROLES.AUTHOR);
  const canAdmin = isAtLeast(ROLES.ADMIN);
  const courseContext = useCourseContext();
  const pathname = usePathname();
  // Тренажёр — отдельный раздел (#623): хедер-таб подсвечивается на /trainer/*.
  const isTrainerActive = pathname.startsWith("/trainer");
  // Глобальный поиск живёт ТОЛЬКО на страницах базы знаний (#623): платформенная
  // (/knowledge-base) и курсовая (/courses/<slug>/knowledge-base) + их под-страницы.
  // Везде ещё хедер без поиска — место освобождено под таб «Тренажёр» и будущие разделы.
  const showHeaderSearch =
    pathname === routes.knowledgeBase ||
    pathname.startsWith(`${routes.knowledgeBase}/`) ||
    /^\/courses\/[^/]+\/knowledge-base(\/|$)/.test(pathname);
  // Course pages link to the course-scoped bookmarks; everywhere else — the
  // global saved page (#510). Auth-gated below: anonymous users have no bookmarks.
  const bookmarksHref = courseContext
    ? routes.courseBookmarks(courseContext.courseSlug)
    : routes.saved;

  return (
    <SidebarProvider
      defaultOpen={defaultSidebarOpen}
      className={cn(accent === "trainer" && "theme-trainer")}
    >
      {/* Празднование повышения уровня (#555) — dialog-портал, монтируется один раз
          на весь авторизованный shell; для анонимов query disabled → no-op. */}
      <LevelUpCelebration />
      {sidebar ?? <AppSidebar />}
      <SidebarInset>
        {/* pt-[safe-area-inset-top] опускает плавающий хедер ниже чёлки/статус-бара
            в iOS PWA standalone (#437); на desktop env()=0, а sm:pt-2 возвращает
            обычный плавающий отступ. */}
        <div className="flex flex-col gap-0 sm:gap-3 p-0 sm:p-2 md:pl-0 pt-[env(safe-area-inset-top)] sm:pt-2 h-svh min-w-0">
          {/* Floating header panel */}
          <header className="relative z-50 flex h-14 shrink-0 items-center gap-3 px-3 md:px-5 bg-surface border-b border-border/50 sm:rounded-2xl sm:border min-w-0 overflow-hidden md:overflow-visible">
            <SeasonalHeaderDecor />
            <div className="relative z-10 flex min-w-0 flex-1 items-center gap-2">
              {/* Контекстная кнопка-таб (#623): в разделе тренажёра — «Платформа»
                  (teal платформы, --platform-primary) обратно на /home; вне его —
                  «⚡ Тренажёр» (фиолетовый) на /trainer. */}
              {isTrainerActive ? (
                <Button
                  variant="ghost"
                  asChild
                  className="hidden h-9 shrink-0 gap-2 rounded-xl px-3 font-medium text-[var(--platform-primary)] hover:bg-[var(--platform-primary)]/10 hover:text-[var(--platform-primary)] md:inline-flex"
                >
                  <Link href={routes.home}>
                    <Icons.compass className="size-4" />
                    Платформа
                  </Link>
                </Button>
              ) : (
                <Button
                  variant="ghost"
                  asChild
                  className="hidden h-9 shrink-0 gap-2 rounded-xl px-3 font-medium text-violet-600 hover:bg-violet-500/12 hover:text-violet-600 md:inline-flex dark:text-violet-300"
                >
                  <Link href={routes.trainer}>
                    <Icons.energy className="size-4" />
                    Тренажёр
                  </Link>
                </Button>
              )}
              {showHeaderSearch && (
                <div className="min-w-0 max-w-4xl flex-1">
                  <GlobalSearch />
                </div>
              )}
            </div>

            <div className="relative z-10 ml-auto flex items-center gap-2">
              <ThemeToggle />
              {/* Notifications live in the header bell on every breakpoint — the
                  bottom bar has no «Уведомления» tab (the page is also reachable from
                  the «Меню» sheet). Renders null for anonymous users. */}
              <NotificationBell topSlot={<TelegramLinkPromptRow />} />
              {user && (
                <Button
                  variant="ghost"
                  size="icon"
                  className="size-9 rounded-xl min-touch"
                  aria-label={courseContext ? "Закладки курса" : "Закладки"}
                  asChild
                >
                  <Link href={bookmarksHref}>
                    <Icons.bookmark size={15} className="text-muted-foreground" />
                  </Link>
                </Button>
              )}

              {isLoading ? (
                <Skeleton className="size-8 rounded-full hidden md:block" />
              ) : user ? (
                <DropdownMenu>
                  <DropdownMenuTrigger asChild>
                    <button className="hidden md:flex items-center gap-2 hover:bg-secondary rounded-xl px-2 py-1.5 transition-colors cursor-pointer">
                      <UserAvatar
                        name={profile?.displayName || profile?.username || user?.name}
                        avatarId={profile?.avatarId}
                        className="size-8 shadow-sm ring-1 ring-primary/20"
                      />
                      <span className="text-sm font-medium hidden lg:inline-block max-w-[120px] truncate">
                        {(profile?.displayName || profile?.username || user?.name)?.split(" ")[0] ||
                          "Пользователь"}
                      </span>
                      <ChevronDown className="size-3.5 text-muted-foreground" />
                    </button>
                  </DropdownMenuTrigger>
                  <DropdownMenuContent align="end" className="w-56">
                    <DropdownMenuLabel className="font-normal">
                      <div className="flex flex-col gap-1">
                        <p className="text-sm font-medium leading-none">
                          {profile?.displayName ||
                            profile?.username ||
                            user?.name ||
                            "Пользователь"}
                        </p>
                        {user.email && (
                          <p className="text-xs text-muted-foreground leading-none">{user.email}</p>
                        )}
                      </div>
                    </DropdownMenuLabel>
                    <DropdownMenuSeparator />
                    <DropdownMenuItem asChild>
                      <Link href={routes.profile}>
                        <User className="mr-2 size-4" />
                        Профиль
                      </Link>
                    </DropdownMenuItem>
                    <DropdownMenuItem asChild>
                      <Link href={routes.settings}>
                        <Settings className="mr-2 size-4" />
                        Настройки
                      </Link>
                    </DropdownMenuItem>
                    {canTeach && (
                      <>
                        <DropdownMenuSeparator />
                        <DropdownMenuItem asChild>
                          <Link href={routes.authorCourses}>
                            <Presentation className="mr-2 size-4" />
                            Преподавание
                          </Link>
                        </DropdownMenuItem>
                      </>
                    )}
                    {canAdmin && (
                      <DropdownMenuItem asChild>
                        <Link href={routes.adminOverview}>
                          <ShieldCheck className="mr-2 size-4" />
                          Администрирование
                        </Link>
                      </DropdownMenuItem>
                    )}
                    <DropdownMenuSeparator />
                    <DropdownMenuItem onClick={() => fullLogout()}>
                      <LogOut className="mr-2 size-4" />
                      Выйти
                    </DropdownMenuItem>
                  </DropdownMenuContent>
                </DropdownMenu>
              ) : (
                <Button variant="default" size="sm" className="gap-2 rounded-xl" asChild>
                  <Link href="/login">
                    <LogIn size={15} />
                    Войти
                  </Link>
                </Button>
              )}
            </div>
          </header>

          {/* Floating content panel */}
          <div className="relative z-0 bg-surface flex-1 min-h-0 overflow-hidden sm:rounded-2xl sm:border sm:border-border/50">
            {/* 56px = MobileBottomNav `h-14` — keep in sync if nav height changes. */}
            <div className="h-full overflow-y-auto pb-[calc(56px+env(safe-area-inset-bottom))] md:pb-0">
              {/* Mobile section nav for author/admin (sticky). Skipped when a
                  custom sidebar is supplied (course / course-builder contexts
                  carry their own CourseTopTabs). Renders null on learning routes. */}
              {!sidebar && <AppSectionTopTabs />}
              <MobileTabTransition>{children}</MobileTabTransition>
            </div>
          </div>
        </div>
        <MobileBottomNav />
        <PwaInstallBanner />
      </SidebarInset>
    </SidebarProvider>
  );
}
