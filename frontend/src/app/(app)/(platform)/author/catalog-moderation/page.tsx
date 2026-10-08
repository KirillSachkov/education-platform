import type { Metadata } from "next";
import { CatalogModerationPage } from "@/features/catalog-moderation";
import { auth } from "@/shared/auth/auth";
import { ROLES } from "@/shared/auth/roles";
import { redirect } from "next/navigation";

export const metadata: Metadata = {
  title: "Модерация витрины",
};

// Очередь модерации каталога — permission `content.moderate` (роль модератора и
// выше). Гейтим server-side как `/author/review` (тот же контур доступа).
export default async function AuthorCatalogModerationPage() {
  const session = await auth();
  const roles = Array.isArray(session?.user?.roles) ? session.user.roles : [];
  const hasAccess =
    roles.includes(ROLES.MODERATOR) || roles.includes(ROLES.ADMIN) || roles.includes(ROLES.OWNER);

  if (!hasAccess) {
    redirect("/");
  }

  return <CatalogModerationPage />;
}
