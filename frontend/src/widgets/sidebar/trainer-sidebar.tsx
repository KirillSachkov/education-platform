"use client";

import { useQuery } from "@tanstack/react-query";
import { trainerLimitsQueryOptions } from "@/entities/trainer-limits";
import { Can, ROLES, useIsAuthenticated } from "@/shared/auth";
import { routes } from "@/shared/config/routes";
import { cn } from "@/shared/lib/css";
import { NavLinkPending } from "@/shared/ui/components";
import { Icons } from "@/shared/ui/icons";
import { LogoMark } from "@/shared/ui/kit/logo";
import {
  Sidebar,
  SidebarContent,
  SidebarFooter,
  SidebarGroup,
  SidebarGroupContent,
  SidebarGroupLabel,
  SidebarHeader,
  SidebarMenu,
  SidebarMenuButton,
  SidebarMenuItem,
  useSidebar,
} from "@/shared/ui/kit/sidebar";
import Link from "next/link";
import { usePathname, useSearchParams } from "next/navigation";

/**
 * Сайдбар раздела «Тренажёр» (#623). Тренажёр вынесен в отдельную часть платформы
 * (route-группа `(trainer)`, см. `app/(app)/(trainer)/trainer/layout.tsx`) — как курс
 * с `CourseSidebar`. Вкладки хаба deep-link'аются через `?tab=` (URL-контракт хаба,
 * `widgets/trainer-hub/lib/hub-state.ts`), активная подсвечивается по query-параметру.
 * На мобиле сайдбар — Sheet (скрыт), внутри хаба остаётся свой in-page таб-бар.
 */
const TRAINER_TABS = [
  { tab: "study", label: "Обучение", icon: Icons.graduation },
  { tab: "mock", label: "Симуляция", icon: Icons.briefcase },
  { tab: "progress", label: "Статистика", icon: Icons.chart },
  { tab: "mistakes", label: "Ошибки", icon: Icons.warning },
  { tab: "bookmarks", label: "Закладки", icon: Icons.bookmark },
] as const;

/**
 * Вкладки админки тренажёра (#623) — зеркалят `AdminTrainerHub` (Контент /
 * Подписка / Статистика), deep-link через `?tab=` на `/trainer/admin`. Видны
 * только админам (`<Can atLeast={ADMIN}>`). Дефолт хаба админки = content.
 */
const TRAINER_ADMIN_TABS = [
  { tab: "content", label: "Контент", icon: Icons.document },
  { tab: "mock", label: "Мок-собесы", icon: Icons.briefcase },
  { tab: "subscription", label: "Подписка", icon: Icons.crown },
  { tab: "stats", label: "Статистика", icon: Icons.chart },
] as const;

export function TrainerSidebar() {
  const pathname = usePathname();
  const searchParams = useSearchParams();
  const { isMobile, setOpenMobile, toggleSidebar, canExpand } = useSidebar();

  // Статус Trainer Pro (#658): не-подписчику пункт «Тренажёр Pro» подсвечиваем как
  // апселл («Оформить»). `isPro` авторитетен — ловит и авто-PRO за полный доступ.
  const isAuthenticated = useIsAuthenticated();
  const limitsQuery = useQuery({ ...trainerLimitsQueryOptions(), enabled: isAuthenticated });
  const hasPro = limitsQuery.data?.isPro === true;

  function closeMobileSidebar() {
    if (isMobile) setOpenMobile(false);
  }

  // Вкладка активна только на самой странице хаба (/trainer), не на под-страницах
  // (/trainer/session/..., /trainer/stats). На хабе дефолт = study.
  const onHubRoot = pathname === routes.trainer;
  const activeTab = onHubRoot ? (searchParams.get("tab") ?? "study") : null;

  // Админка тренажёра — отдельная страница /trainer/admin со своими вкладками (?tab=).
  const onAdminRoot = pathname === routes.trainerAdmin;
  const activeAdminTab = onAdminRoot ? (searchParams.get("tab") ?? "content") : null;

  return (
    <Sidebar variant="floating" collapsible="icon">
      <SidebarHeader className="h-14 py-0 justify-center">
        <SidebarMenu>
          <SidebarMenuItem>
            <SidebarMenuButton size="lg" asChild>
              <Link
                href={routes.trainer}
                className="group-data-[collapsible=icon]:justify-center"
                onClick={closeMobileSidebar}
              >
                <LogoMark size={22} className="text-primary" />
                <div className="grid flex-1 text-left text-sm leading-tight group-data-[collapsible=icon]:hidden">
                  <span className="truncate font-semibold">SachkovLearn</span>
                </div>
              </Link>
            </SidebarMenuButton>
          </SidebarMenuItem>
        </SidebarMenu>
      </SidebarHeader>

      {/* Кнопка свернуть/развернуть — как в основном сайдбаре платформы (AppSidebar). */}
      {canExpand && (
        <SidebarGroup className="py-1">
          <SidebarGroupContent>
            <SidebarMenu>
              <SidebarMenuItem>
                <SidebarMenuButton onClick={toggleSidebar} tooltip="Свернуть">
                  <Icons.sidebarToggle />
                  <span>Свернуть</span>
                </SidebarMenuButton>
              </SidebarMenuItem>
            </SidebarMenu>
          </SidebarGroupContent>
        </SidebarGroup>
      )}

      {/* Divider */}
      <div className="mx-4 h-px bg-sidebar-border" />

      <SidebarContent>
        <SidebarGroup>
          <SidebarGroupLabel>Тренажёр</SidebarGroupLabel>
          <SidebarGroupContent>
            <SidebarMenu>
              {TRAINER_TABS.map((item) => (
                <SidebarMenuItem key={item.tab}>
                  <SidebarMenuButton
                    asChild
                    isActive={activeTab === item.tab}
                    tooltip={item.label}
                  >
                    <Link
                      href={item.tab === "study" ? routes.trainer : routes.trainerTab(item.tab)}
                      onClick={closeMobileSidebar}
                    >
                      <item.icon />
                      <span>{item.label}</span>
                      <NavLinkPending />
                    </Link>
                  </SidebarMenuButton>
                </SidebarMenuItem>
              ))}
            </SidebarMenu>
          </SidebarGroupContent>
        </SidebarGroup>

        {/* Подписка «Тренажёр Pro» (#658) — отдельным заметным пунктом, не в «Ещё».
            Не-подписчику — фиолетовый акцент + пометка «Оформить» (апселл). /trainer/pro
            живёт внутри раздела тренажёра, это не «увод» на платформу. */}
        <SidebarGroup className="py-1">
          <SidebarGroupContent>
            <SidebarMenu>
              <SidebarMenuItem>
                <SidebarMenuButton
                  asChild
                  isActive={pathname.startsWith(routes.trainerPro)}
                  tooltip="Тренажёр Pro"
                  className={cn(
                    !hasPro &&
                      "text-violet-600 hover:text-violet-700 dark:text-violet-300 dark:hover:text-violet-200",
                  )}
                >
                  <Link href={routes.trainerPro} onClick={closeMobileSidebar}>
                    <Icons.crown className={cn(!hasPro && "text-violet-500 dark:text-violet-300")} />
                    <span>Тренажёр Pro</span>
                    <NavLinkPending />
                    {!hasPro && (
                      <span className="ml-auto rounded-md bg-violet-500/15 px-1.5 py-0.5 text-[10px] font-semibold text-violet-600 group-data-[collapsible=icon]:hidden dark:text-violet-300">
                        Оформить
                      </span>
                    )}
                  </Link>
                </SidebarMenuButton>
              </SidebarMenuItem>
            </SidebarMenu>
          </SidebarGroupContent>
        </SidebarGroup>

        <Can atLeast={ROLES.ADMIN}>
          <SidebarGroup>
            <SidebarGroupLabel>Администрирование</SidebarGroupLabel>
            <SidebarGroupContent>
              <SidebarMenu>
                {TRAINER_ADMIN_TABS.map((item) => (
                  <SidebarMenuItem key={item.tab}>
                    <SidebarMenuButton
                      asChild
                      isActive={activeAdminTab === item.tab}
                      tooltip={item.label}
                    >
                      <Link
                        href={
                          item.tab === "content"
                            ? routes.trainerAdmin
                            : `${routes.trainerAdmin}?tab=${item.tab}`
                        }
                        onClick={closeMobileSidebar}
                      >
                        <item.icon />
                        <span>{item.label}</span>
                        <NavLinkPending />
                      </Link>
                    </SidebarMenuButton>
                  </SidebarMenuItem>
                ))}
              </SidebarMenu>
            </SidebarGroupContent>
          </SidebarGroup>
        </Can>

        <SidebarGroup>
          <SidebarGroupLabel>Ещё</SidebarGroupLabel>
          <SidebarGroupContent>
            <SidebarMenu>
              <SidebarMenuItem>
                <SidebarMenuButton
                  asChild
                  isActive={pathname.startsWith(routes.levelTest)}
                  tooltip="Тест уровня"
                >
                  <Link href={routes.levelTest} onClick={closeMobileSidebar}>
                    <Icons.levelTest />
                    <span>Тест уровня</span>
                    <NavLinkPending />
                  </Link>
                </SidebarMenuButton>
              </SidebarMenuItem>
            </SidebarMenu>
          </SidebarGroupContent>
        </SidebarGroup>
      </SidebarContent>

      <SidebarFooter>
        {/* Приписка раздела — снизу: шапка остаётся платформенным брендом (логотип + SachkovLearn). */}
        <div className="px-2 pt-1 group-data-[collapsible=icon]:hidden">
          <p className="flex items-center gap-1.5 text-[11px] font-semibold uppercase tracking-wider text-violet-500/90 dark:text-violet-300/90">
            <Icons.energy className="size-3" />
            Тренажёр
          </p>
          <p className="text-xs text-muted-foreground">Подготовка к собеседованиям</p>
        </div>
        <SidebarMenu>
          <SidebarMenuItem>
            <SidebarMenuButton asChild tooltip="На платформу">
              <Link href={routes.home} onClick={closeMobileSidebar}>
                <Icons.compass />
                <span>На платформу</span>
                <NavLinkPending />
              </Link>
            </SidebarMenuButton>
          </SidebarMenuItem>
        </SidebarMenu>
      </SidebarFooter>
    </Sidebar>
  );
}
