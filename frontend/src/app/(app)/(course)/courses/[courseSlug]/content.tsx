"use client";

import { CourseHome } from "@/features/course-learning";
import { EnrollCard } from "@/features/course-enroll";
import { CoursePurchaseCta } from "@/features/course-purchase-options";
import type { CourseCurriculumDto } from "@/entities/course";
import { useCourseId } from "@/shared/providers/course-id-provider";
import { useTrackGrowthView } from "@/shared/analytics";

export function CoursePageContent({
  initialCourse,
}: {
  initialCourse?: CourseCurriculumDto | null;
}) {
  const courseId = useCourseId();
  useTrackGrowthView(
    { name: "course_view", properties: { course_id: courseId } },
    courseId ? `course:${courseId}` : "",
  );

  return (
    <CourseHome
      courseId={courseId}
      initialCourse={initialCourse ?? null}
      // Композиция на page-уровне: EnrollCard + CTA «этот курс входит в планы»
      // (#404) как siblings — без cross-feature import'а внутри course-learning.
      renderEnrollCard={(props) => (
        <div className="flex flex-col gap-3">
          <EnrollCard
            courseId={props.courseId}
            hasFreeContent={props.hasFreeContent}
            state={props.state}
            startHref={props.startHref}
          />
          {props.state !== "enrolled" ? <CoursePurchaseCta courseId={props.courseId} /> : null}
        </div>
      )}
    />
  );
}
