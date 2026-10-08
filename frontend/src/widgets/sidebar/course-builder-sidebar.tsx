"use client";

import Link from "next/link";
import { usePathname, useSearchParams } from "next/navigation";
import { useQuery } from "@tanstack/react-query";
import {
  BarChart3,
  FileText,
  FolderKanban,
  Globe,
  LayoutGrid,
  Layers,
  Loader2,
  Map,
  Settings2,
  Users2,
} from "lucide-react";
import { Icons } from "@/shared/ui/icons";
import { NavLinkPending } from "@/shared/ui/components";
import {
  Sidebar,
  SidebarContent,
  SidebarGroup,
  SidebarGroupContent,
  SidebarGroupLabel,
  SidebarHeader,
  SidebarMenu,
  SidebarMenuButton,
  SidebarMenuItem,
  useSidebar,
} from "@/shared/ui/kit/sidebar";
import { Badge } from "@/shared/ui/kit/badge";
import { routes } from "@/shared/config/routes";
import { useCourseId, useCourseSlug } from "@/shared/providers/course-id-provider";
import { courseBuilderQueryOptions } from "@/entities/course";
import { cn } from "@/shared/lib/css";

type TabId =
  | "modules"
  | "projects"
  | "materials"
  | "collections"
  | "landing"
  | "settings"
  | "students"
  | "statistics";

const tabs: {
  id: TabId;
  label: string;
  icon: typeof Layers;
}[] = [
  { id: "modules", label: "Модули", icon: Layers },
  { id: "projects", label: "Проекты", icon: FolderKanban },
  { id: "materials", label: "Материалы", icon: FileText },
  { id: "collections", label: "Подборки", icon: LayoutGrid },
  { id: "landing", label: "Лендинг", icon: Globe },
  { id: "settings", label: "Настройки", icon: Settings2 },
  { id: "students", label: "Студенты", icon: Users2 },
  { id: "statistics", label: "Статистика", icon: BarChart3 },
];

export function CourseBuilderSidebar() {
  const pathname = usePathname();
  const searchParams = useSearchParams();
  const { isMobile, setOpenMobile, toggleSidebar, canExpand } = useSidebar();

  const courseId = useCourseId();
  const courseSlug = useCourseSlug();

  const { data: course, isLoading } = useQuery(courseBuilderQueryOptions(courseId));

  function closeMobileSidebar() {
    if (isMobile) setOpenMobile(false);
  }

  const basePath = routes.authorCourseBuilder(courseSlug);
  const roadmapPath = routes.authorCourseRoadmap(courseSlug);
  const isOnBuilder = pathname === basePath;
  const isOnRoadmap = pathname.startsWith(roadmapPath);

  const activeTab = (searchParams.get("tab") as TabId | null) ?? "modules";

  const statusDot =
    course?.status === "PUBLISHED"
      ? "bg-teal"
      : course?.status === "DRAFT"
        ? "bg-yellow"
        : "bg-red";

  const statusLabel =
    course?.status === "PUBLISHED"
      ? "Опубликован"
      : course?.status === "DRAFT"
        ? "Черновик"
        : course?.status === "ARCHIVED"
          ? "Архив"
          : "";

  return (
    <Sidebar variant="floating" collapsible="icon">
      <SidebarHeader className="py-3 gap-3">
        <SidebarMenu>
          <SidebarMenuItem>
            <SidebarMenuButton asChild tooltip="Назад к пространству" size="sm">
              <Link
                href={routes.authorCourses}
                className="gap-2"
                onClick={closeMobileSidebar}
              >
                <Icons.back className="size-4 shrink-0" />
                <span className="text-xs text-sidebar-foreground/60 group-data-[collapsible=icon]:hidden">
                  Назад к пространству
                </span>
                <NavLinkPending />
              </Link>
            </SidebarMenuButton>
          </SidebarMenuItem>
          {canExpand && (
            <SidebarMenuItem>
              <SidebarMenuButton onClick={toggleSidebar} tooltip="Свернуть" size="sm">
                <Icons.sidebarToggle className="size-4 shrink-0" />
                <span className="text-xs group-data-[collapsible=icon]:hidden">
                  Свернуть
                </span>
              </SidebarMenuButton>
            </SidebarMenuItem>
          )}
        </SidebarMenu>

        {/* Collapsed: show just an accent dot */}
        <div className="hidden group-data-[collapsible=icon]:flex justify-center py-1">
          <div className={cn("size-2 rounded-full", statusDot)} />
        </div>

        {/* Expanded: course card with title + status */}
        <div className="group-data-[collapsible=icon]:hidden">
          <div className="mx-1 rounded-xl bg-gradient-to-br from-primary/8 to-primary/3 border border-primary/10 px-3 py-2.5">
            <div className="flex items-start gap-2">
              <div className={cn("mt-1.5 size-2 rounded-full shrink-0", statusDot)} />
              <div className="min-w-0 flex-1">
                <h2 className="text-[13px] font-semibold leading-tight line-clamp-2 text-sidebar-foreground">
                  {isLoading ? (
                    <span className="inline-flex items-center gap-1.5 text-sidebar-foreground/50">
                      <Loader2 className="size-3 animate-spin" /> Загрузка…
                    </span>
                  ) : (
                    course?.title ?? "Курс не найден"
                  )}
                </h2>
                {statusLabel && (
                  <p className="mt-0.5 text-[11px] text-sidebar-foreground/50">
                    {statusLabel}
                  </p>
                )}
              </div>
            </div>
          </div>
        </div>
      </SidebarHeader>

      <SidebarContent>
        <SidebarGroup className="py-1.5">
          <SidebarGroupLabel>Разделы</SidebarGroupLabel>
          <SidebarGroupContent>
            <SidebarMenu>
              {tabs.map((tab) => {
                const Icon = tab.icon;
                const isActive = isOnBuilder && activeTab === tab.id;
                const href = `${basePath}?tab=${tab.id}`;
                return (
                  <SidebarMenuItem key={tab.id}>
                    <SidebarMenuButton
                      asChild
                      isActive={isActive}
                      tooltip={tab.label}
                    >
                      <Link href={href} onClick={closeMobileSidebar}>
                        <Icon />
                        <span>{tab.label}</span>
                        <NavLinkPending />
                      </Link>
                    </SidebarMenuButton>
                  </SidebarMenuItem>
                );
              })}
              <SidebarMenuItem>
                <SidebarMenuButton
                  asChild
                  isActive={isOnRoadmap}
                  tooltip="Роадмап"
                >
                  <Link href={roadmapPath} onClick={closeMobileSidebar}>
                    <Map />
                    <span>Роадмап</span>
                    {course?.status === "DRAFT" && (
                      <Badge
                        variant="secondary"
                        className="ml-auto text-[10px] px-1.5 py-0"
                      >
                        beta
                      </Badge>
                    )}
                    <NavLinkPending />
                  </Link>
                </SidebarMenuButton>
              </SidebarMenuItem>
            </SidebarMenu>
          </SidebarGroupContent>
        </SidebarGroup>
      </SidebarContent>
    </Sidebar>
  );
}
