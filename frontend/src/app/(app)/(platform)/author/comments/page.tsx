import type { Metadata } from "next";
import { redirect } from "next/navigation";
import { AuthorCommentsFeed } from "@/widgets/author-comments-feed";
import { auth } from "@/shared/auth/auth";
import { ROLES } from "@/shared/auth/roles";

export const metadata: Metadata = {
  title: "Комментарии",
};

export default async function AuthorCommentsPage() {
  const session = await auth();
  const roles = Array.isArray(session?.user?.roles) ? session.user.roles : [];
  const hasAccess =
    roles.includes(ROLES.AUTHOR) ||
    roles.includes(ROLES.MODERATOR) ||
    roles.includes(ROLES.ADMIN) ||
    roles.includes(ROLES.OWNER);

  if (!hasAccess) {
    redirect("/");
  }

  return <AuthorCommentsFeed />;
}
