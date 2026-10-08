import type { Metadata } from "next";
import type { CourseCurriculumDto } from "@/entities/course";
import { APP_URL } from "@/shared/config/site";
import { CourseJsonLd, buildEntityMetadata, fetchAnonymous } from "@/shared/seo";
import { CoursePageContent } from "./content";

interface Props {
  params: Promise<{ courseSlug: string }>;
}

interface CourseSlugResolved {
  courseId: string;
  slug: string;
}

async function fetchCourseCurriculum(courseSlug: string): Promise<CourseCurriculumDto | null> {
  const resolved = await fetchAnonymous<CourseSlugResolved>(`/courses/by-slug/${courseSlug}/`);
  if (!resolved?.courseId) return null;
  return fetchAnonymous<CourseCurriculumDto>(`/courses/${resolved.courseId}/curriculum/`);
}

export async function generateMetadata({ params }: Props): Promise<Metadata> {
  const { courseSlug } = await params;
  const course = await fetchCourseCurriculum(courseSlug);
  if (!course?.title) return { title: "Курс" };

  return buildEntityMetadata({
    title: course.title,
    description: course.description,
    imageUrl: course.imageUrl,
    path: `/courses/${courseSlug}`,
    type: "website",
    fullTitle: `${course.title} — курс на SachkovLearn`,
  });
}

export default async function SpaceCoursePage({ params }: Props) {
  const { courseSlug } = await params;
  const course = await fetchCourseCurriculum(courseSlug);
  return (
    <>
      {course?.title && (
        <CourseJsonLd
          name={course.title}
          description={course.description}
          providerName="SachkovLearn"
          providerUrl={APP_URL}
          instructorName={course.authorDisplayName ?? "SachkovLearn"}
          url={`${APP_URL}/courses/${courseSlug}`}
          image={
            course.imageUrl
              ? course.imageUrl.startsWith("http")
                ? course.imageUrl
                : `${APP_URL}${course.imageUrl}`
              : undefined
          }
          inLanguage="ru"
        />
      )}
      <CoursePageContent initialCourse={course} />
    </>
  );
}
