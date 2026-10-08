import type { UserCourseProgressDto } from "@/entities/enrollment";

export function courseProgressCardKey(course: UserCourseProgressDto): string {
  return course.courseId;
}
