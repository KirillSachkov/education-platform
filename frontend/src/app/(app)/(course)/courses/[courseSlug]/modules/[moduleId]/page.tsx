import { redirect } from "next/navigation";
import { routes } from "@/shared/config/routes";

// Dedicated module pages were removed — all module breadcrumbs/links now resolve
// to the program page with `?section=<moduleId>`. Kept as a server redirect for
// any lingering bookmarks pointing at the old URL.
export default async function ModuleRedirect({
  params,
}: {
  params: Promise<{ courseSlug: string; moduleId: string }>;
}) {
  const { courseSlug, moduleId } = await params;
  redirect(routes.courseModule(courseSlug, moduleId));
}
