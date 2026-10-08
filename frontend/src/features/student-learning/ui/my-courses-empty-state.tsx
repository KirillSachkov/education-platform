"use client";

import { EmptyState } from "@/shared/ui/kit/empty-state";
import { Icons } from "@/shared/ui/icons";
import { Button } from "@/shared/ui/kit/button";
import Link from "next/link";

export function MyCoursesEmptyState() {
  return (
    <EmptyState
      variant="card"
      icon={Icons.library}
      title="У вас пока нет курсов"
      description="Выберите курс, с которого хотите начать."
      action={
        <Button asChild>
          <Link href="/">Перейти к пространствам</Link>
        </Button>
      }
    />
  );
}
