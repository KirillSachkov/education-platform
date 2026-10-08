"use client";

import Link from "next/link";
import { MyXpProgressCard } from "@/entities/user-progress";
import { routes } from "@/shared/config/routes";
import { Icons } from "@/shared/ui/icons";

export function LevelProgressSection() {
  return (
    <div className="space-y-2">
      <MyXpProgressCard />
      <Link
        href={routes.progress}
        className="inline-flex items-center gap-1 text-xs text-muted-foreground transition-colors hover:text-foreground"
      >
        Мой прогресс
        <Icons.arrowRight size={12} />
      </Link>
    </div>
  );
}
