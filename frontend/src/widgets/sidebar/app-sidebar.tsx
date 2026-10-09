"use client";

import { useRoles } from "@/shared/auth";
import { routes } from "@/shared/config/routes";
import {
  accountNav,
  adminNav,
  canViewNavItem,
  learningNav,
  teachingNav,
} from "@/shared/config/app-navigation";
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
import { SidebarPlanBadge } from "./sidebar-plan-badge";
import { LogoMark } from "@/shared/ui/kit/logo";
import { Icons } from "@/shared/ui/icons";
import { NavLinkPending } from "@/shared/ui/components";
import Link from "next/link";
import { usePathname } from "next/navigation";

export function AppSidebar() {
  const pathname = usePathname();
  const { isAtLeast, hasAnyRole } = useRoles();
  const { toggleSidebar, isMobile, setOpenMobile, canExpand } = useSidebar();

  function closeMobileSidebar() {
    if (isMobile) setOpenMobile(false);
  }

  const isAdminView = pathname.startsWith("/admin");
  const isTeachingView = pathname.startsWith("/author");
  const isAccountView =
    pathname.startsWith("/profile") ||
    pathname.startsWith("/notifications") ||
    pathname.startsWith("/settings") ||
    pathname.startsWith("/payments") ||
    pathname.startsWith("/my-plans");
  const isLearningView = !isAdminView && !isTeachingView && !isAccountView;

  // Логотип = контекстный «шаг вверх»: из под-контекста (профиль/админка/преподавание)
  // ведём на учебную главную, из самой учебной зоны — на публичный лендинг.
  const logoHref = isLearningView ? routes.landing : routes.home;

  return (
    <Sidebar variant="floating" collapsible="icon">
      <SidebarHeader className="h-14 py-0 justify-center">
        <SidebarMenu>
          <SidebarMenuItem>
            <SidebarMenuButton size="lg" asChild>
              <Link href={logoHref} className="group-data-[collapsible=icon]:justify-center">
                <LogoMark size={22} className="text-primary" />
                <div className="grid flex-1 text-left text-sm leading-tight group-data-[collapsible=icon]:hidden">
                  <span className="truncate font-semibold">SachkovLearn</span>
                  <span className="truncate text-xs text-muted-foreground">
                    {isAdminView
                      ? "Администрирование"
                      : isTeachingView
                        ? "Преподавание"
                        : isAccountView
                          ? "Аккаунт"
                          : "Обучение"}
                  </span>
                </div>
              </Link>
            </SidebarMenuButton>
          </SidebarMenuItem>
        </SidebarMenu>
      </SidebarHeader>

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
        {isAdminView ? (
          <SidebarGroup>
            <SidebarGroupLabel>Администрирование</SidebarGroupLabel>
            <SidebarGroupContent>
              <SidebarMenu>
                {adminNav.map((item) => (
                  <SidebarMenuItem key={item.href}>
                    <SidebarMenuButton
                      asChild
                      isActive={pathname.startsWith(item.href)}
                      tooltip={item.label}
                    >
                      <Link href={item.href} onClick={closeMobileSidebar}>
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
        ) : isTeachingView ? (
          <SidebarGroup>
            <SidebarGroupLabel>Преподавание</SidebarGroupLabel>
            <SidebarGroupContent>
              <SidebarMenu>
                {teachingNav
                  .filter((item) => canViewNavItem(item, { isAtLeast, hasAnyRole }))
                  .map((item) => (
                    <SidebarMenuItem key={item.href}>
                      <SidebarMenuButton
                        asChild
                        isActive={pathname.startsWith(item.href)}
                        tooltip={item.label}
                      >
                        <Link href={item.href} onClick={closeMobileSidebar}>
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
        ) : isAccountView ? (
          <SidebarGroup>
            <SidebarGroupLabel>Аккаунт</SidebarGroupLabel>
            <SidebarGroupContent>
              <SidebarMenu>
                {accountNav.map((item) => (
                  <SidebarMenuItem key={item.href}>
                    <SidebarMenuButton
                      asChild
                      isActive={pathname === item.href || pathname.startsWith(`${item.href}/`)}
                      tooltip={item.label}
                    >
                      <Link href={item.href} onClick={closeMobileSidebar}>
                        <item.icon />
                        <span>{item.label}</span>
                        <NavLinkPending />
                      </Link>
                    </SidebarMenuButton>
                  </SidebarMenuItem>
                ))}
                <SidebarMenuItem>
                  <SidebarMenuButton asChild tooltip="На главную">
                    <Link href={routes.home} onClick={closeMobileSidebar}>
                      <Icons.compass />
                      <span>На главную</span>
                      <NavLinkPending />
                    </Link>
                  </SidebarMenuButton>
                </SidebarMenuItem>
              </SidebarMenu>
            </SidebarGroupContent>
          </SidebarGroup>
        ) : (
          <SidebarGroup>
            <SidebarGroupLabel>Обучение</SidebarGroupLabel>
            <SidebarGroupContent>
              <SidebarMenu>
                {learningNav.map((item) => {
                  const isActive = item.exact
                    ? pathname === item.href
                    : pathname === item.href || pathname.startsWith(`${item.href}/`);
                  return (
                    <SidebarMenuItem key={item.href}>
                      <SidebarMenuButton asChild isActive={isActive} tooltip={item.label}>
                        <Link href={item.href} onClick={closeMobileSidebar}>
                          <item.icon />
                          <span>{item.label}</span>
                          <NavLinkPending />
                        </Link>
                      </SidebarMenuButton>
                    </SidebarMenuItem>
                  );
                })}
              </SidebarMenu>
            </SidebarGroupContent>
          </SidebarGroup>
        )}
      </SidebarContent>

      <SidebarFooter>
        <SidebarPlanBadge />
      </SidebarFooter>
    </Sidebar>
  );
}
