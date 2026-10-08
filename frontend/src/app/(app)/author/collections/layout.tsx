import { auth } from "@/shared/auth/auth";
import { ROLES } from "@/shared/auth/roles";
import { AuthorContentShell } from "@/widgets/layouts";
import { redirect } from "next/navigation";

export default async function AuthorCollectionsLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  const session = await auth();
  const roles = Array.isArray(session?.user?.roles) ? session.user.roles : [];
  const hasAccess =
    roles.includes(ROLES.AUTHOR) ||
    roles.includes(ROLES.EDITOR) ||
    roles.includes(ROLES.MODERATOR) ||
    roles.includes(ROLES.ADMIN) ||
    roles.includes(ROLES.OWNER);

  if (!hasAccess) {
    redirect("/");
  }

  return <AuthorContentShell>{children}</AuthorContentShell>;
}
