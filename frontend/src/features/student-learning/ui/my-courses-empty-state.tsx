"use client";

import { EmptyState } from "@/shared/ui/kit/empty-state";
import { Icons } from "@/shared/ui/icons";
import { Button } from "@/shared/ui/kit/button";
import Link from "next/link";
import { routes } from "@/shared/config/routes";

export function MyCoursesEmptyState() {
  return (
    <EmptyState
      variant="card"
      icon={Icons.library}
      title="У вас пока нет курсов"
      description="Здесь появятся курсы, к которым вам выдан доступ."
      action={
        <Button asChild>
          <Link href={routes.myPlans}>Мои доступы</Link>
        </Button>
      }
    />
  );
}
