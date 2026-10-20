import { ROLES, type Role } from "@/shared/auth";
import { routes } from "@/shared/config/routes";
import { Icons, type IconComponent } from "@/shared/ui/icons";

/**
 * Single source of truth for the platform's context navigation groups.
 *
 * Consumed by both the desktop `AppSidebar` and the mobile `AppSectionTopTabs`
 * so the two never drift — add/remove/rename a section in one place. The `icon`
 * is used by the sidebar; the mobile tabs render labels only.
 */
export type NavItem = {
  href: string;
  icon: IconComponent;
  label: string;
  roles?: readonly Role[];
  minRole?: Role;
  exact?: boolean;
};

export const learningNav: NavItem[] = [
  { href: routes.home, icon: Icons.home, label: "Моё обучение", exact: true },
  { href: routes.saved, icon: Icons.bookmark, label: "Сохранённое" },
  { href: routes.pricing, icon: Icons.crown, label: "Доступ" },
];

export const teachingNav: NavItem[] = [
  { href: routes.authorCourses, icon: Icons.editAlt, label: "Курсы" },
  { href: routes.authorKnowledgeBase, icon: Icons.document, label: "Материалы" },
  { href: routes.authorCollections, icon: Icons.grid, label: "Подборки" },
  { href: routes.authorQuizzes, icon: Icons.quiz, label: "Тесты" },
  {
    href: routes.authorReview,
    icon: Icons.clipboardCheck,
    label: "Проверка работ",
    minRole: ROLES.MODERATOR,
  },
  {
    href: routes.authorCatalogModeration,
    icon: Icons.shieldCheck,
    label: "Модерация витрины",
    minRole: ROLES.MODERATOR,
  },
  {
    href: routes.authorComments,
    icon: Icons.message,
    label: "Комментарии",
    roles: [ROLES.AUTHOR, ROLES.MODERATOR, ROLES.ADMIN, ROLES.OWNER],
  },
];

export function canViewNavItem(
  item: NavItem,
  checks: {
    isAtLeast: (role: Role) => boolean;
    hasAnyRole: (roles: Role[]) => boolean;
  },
) {
  if (item.roles) {
    return checks.hasAnyRole([...item.roles]);
  }

  return !item.minRole || checks.isAtLeast(item.minRole);
}

export const adminNav: NavItem[] = [
  { href: routes.adminOverview, icon: Icons.dashboard, label: "Обзор" },
  { href: routes.adminPlans, icon: Icons.crown, label: "Планы доступа" },
  { href: routes.adminUsers, icon: Icons.userSettings, label: "Пользователи" },
  { href: routes.adminAuditLog, icon: Icons.clock, label: "Audit log" },
  { href: routes.adminPayments, icon: Icons.gift, label: "Платежи" },
  { href: routes.adminCampaigns, icon: Icons.send, label: "Рассылки" },
  { href: routes.adminTests, icon: Icons.chart, label: "Статистика тестов" },
  { href: routes.adminAiModels, icon: Icons.settings, label: "AI модели" },
];

export const accountNav: NavItem[] = [
  { href: routes.profile, icon: Icons.user, label: "Профиль" },
  { href: routes.notifications, icon: Icons.notification, label: "Уведомления" },
  { href: routes.myPlans, icon: Icons.crown, label: "Мои планы" },
  { href: routes.payments, icon: Icons.creditCard, label: "Платежи" },
  { href: routes.settings, icon: Icons.settings, label: "Настройки" },
];
