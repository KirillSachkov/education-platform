import type { Metadata } from "next";
import { AuthorCoursesList } from "@/features/author-courses";

export const metadata: Metadata = {
  title: "Курсы",
};

export default function AuthorCoursesPage() {
  return <AuthorCoursesList />;
}
