"use client";

import Link from "next/link";
import { routes } from "@/shared/config/routes";

export default function CourseNotFound() {
  return (
    <div className="flex flex-col items-center justify-center h-full gap-4 p-6">
      <h2 className="text-lg font-semibold">Курс не найден</h2>
      <p className="text-muted-foreground">Проверьте правильность ссылки</p>
      <Link href={routes.courses} className="text-primary underline text-sm">
        К курсам
      </Link>
    </div>
  );
}
