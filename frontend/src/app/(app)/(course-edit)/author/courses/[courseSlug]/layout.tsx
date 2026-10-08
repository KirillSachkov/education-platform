import { AppLayout } from "@/widgets/layouts/app-layout";
import { CourseBuilderSidebar } from "@/widgets/sidebar";
import { readSidebarDefaultOpen } from "@/shared/lib/sidebar-server";
import { AuthorCourseResolver } from "./author-course-resolver";

export default async function AuthorCourseLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  const defaultSidebarOpen = await readSidebarDefaultOpen();
  return (
    <AuthorCourseResolver>
      <AppLayout sidebar={<CourseBuilderSidebar />} defaultSidebarOpen={defaultSidebarOpen}>
        {children}
      </AppLayout>
    </AuthorCourseResolver>
  );
}
