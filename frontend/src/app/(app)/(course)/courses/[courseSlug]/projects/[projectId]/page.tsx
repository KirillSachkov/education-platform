import { redirect } from "next/navigation";
import { routes } from "@/shared/config/routes";

// Dedicated project pages were removed — all project breadcrumbs/links now resolve
// to the program page (projects tab) with `?section=<projectId>`. Kept as a server
// redirect for any lingering bookmarks pointing at the old URL.
export default async function ProjectRedirect({
  params,
}: {
  params: Promise<{ courseSlug: string; projectId: string }>;
}) {
  const { courseSlug, projectId } = await params;
  redirect(routes.courseProject(courseSlug, projectId));
}
