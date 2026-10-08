import { AppLayout } from "@/widgets/layouts/app-layout";
import { CourseSidebar } from "@/widgets/sidebar";
import { MobileCurriculumFab } from "@/widgets/sidebar/mobile-curriculum-fab";
import { CourseTopTabs } from "@/widgets/course-top-tabs";
import { readSidebarDefaultOpen } from "@/shared/lib/sidebar-server";
import { fetchAnonymous } from "@/shared/seo";
import { CourseSlugResolver } from "./course-slug-resolver";

interface ResolvedCourse {
  courseId: string;
  slug: string;
}

export default async function SpaceCourseLayout({
  children,
  params,
}: {
  children: React.ReactNode;
  params: Promise<{ courseSlug: string }>;
}) {
  const { courseSlug } = await params;
  const [defaultSidebarOpen, initialCourse] = await Promise.all([
    readSidebarDefaultOpen(),
    fetchAnonymous<ResolvedCourse>(`/courses/by-slug/${courseSlug}/`),
  ]);
  return (
    <CourseSlugResolver courseSlug={courseSlug} initialCourse={initialCourse}>
      <AppLayout sidebar={<CourseSidebar />} defaultSidebarOpen={defaultSidebarOpen}>
        <CourseTopTabs />
        {children}
        <MobileCurriculumFab />
      </AppLayout>
    </CourseSlugResolver>
  );
}
