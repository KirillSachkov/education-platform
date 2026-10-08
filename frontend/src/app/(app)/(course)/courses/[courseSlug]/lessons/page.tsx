"use client";

import { useEffect } from "react";
import { useRouter } from "next/navigation";
import { routes } from "@/shared/config/routes";
import { useCourseSlug } from "@/shared/providers/course-id-provider";
import { Icons } from "@/shared/ui/icons";

// Legacy URL — страница Уроков объединена с Заданиями во вкладку «Программа».
// Сохраняем редирект, чтобы старые закладки и ссылки не 404-или.
export default function LessonsRedirectPage() {
  const router = useRouter();
  const courseSlug = useCourseSlug();

  useEffect(() => {
    router.replace(routes.courseProgram(courseSlug));
  }, [router, courseSlug]);

  return (
    <div className="flex items-center justify-center h-full">
      <Icons.loading className="size-6 animate-spin text-muted-foreground" />
    </div>
  );
}
