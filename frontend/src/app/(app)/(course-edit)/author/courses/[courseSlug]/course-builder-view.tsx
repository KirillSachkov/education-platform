"use client";

import { CourseBuilder } from "@/features/course-builder";
import { CourseStatistics } from "@/features/course-statistics";
import { CourseStudents } from "@/features/course-students";
import { BroadcastCourseDialog } from "@/features/notifications-broadcast";
import { TransferCourseAuthorDialog } from "@/features/transfer-course-author";
import { Can, ROLES } from "@/shared/auth";
import { useCourseId } from "@/shared/providers/course-id-provider";

export function CourseBuilderView() {
  const courseId = useCourseId();
  return (
    <CourseBuilder
      courseId={courseId}
      renderStudents={({ courseId: id, course }) => (
        <CourseStudents courseId={id} course={course} />
      )}
      renderStatistics={({ courseId: id, course }) => (
        <CourseStatistics courseId={id} course={course} />
      )}
      renderBroadcastDialog={({ courseId: id, course }) => (
        <BroadcastCourseDialog courseId={id} courseTitle={course.title} />
      )}
      renderTransferDialog={({ courseId: id, course }) => (
        <Can roles={[ROLES.MODERATOR, ROLES.ADMIN, ROLES.OWNER]}>
          <TransferCourseAuthorDialog courseId={id} courseTitle={course.title} />
        </Can>
      )}
    />
  );
}
