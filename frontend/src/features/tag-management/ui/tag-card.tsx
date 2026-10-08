"use client";

import type { ReactNode } from "react";
import Link from "next/link";
import type { TagDto } from "@/entities/tag";
import { cn } from "@/shared/lib/css";
import { Card } from "@/shared/ui/kit/card";
import { TagStatusBadge } from "./tag-status-badge";

interface TagCardProps {
  tag: Pick<TagDto, "id" | "title" | "kind">;
  href?: string;
  actionSlot?: ReactNode;
  className?: string;
}

export function TagCard({
  tag,
  href,
  actionSlot,
  className,
}: TagCardProps) {
  const title = (
    <h3 className="line-clamp-2 text-[1.65rem] font-semibold leading-none tracking-[-0.02em] transition-colors group-hover:text-primary">
      {tag.title}
    </h3>
  );

  return (
    <Card
      className={cn(
        "group overflow-hidden border-border/70 bg-card/80 p-3.5 transition-colors hover:border-primary/20",
        className,
      )}
    >
      <div className="flex min-h-[92px] flex-col">
        <div className="flex items-center justify-between gap-2">
          <TagStatusBadge
            kind={tag.kind}
            className="h-7 rounded-full px-2.5 text-[11px] font-medium"
          />
          {actionSlot ? (
            <div className="flex items-center gap-1 text-muted-foreground">
              {actionSlot}
            </div>
          ) : null}
        </div>

        {href ? (
          <Link
            href={href}
            className="mt-4 flex flex-1 rounded-lg outline-none transition-colors focus-visible:ring-2 focus-visible:ring-ring/50"
          >
            {title}
          </Link>
        ) : (
          <div className="mt-4 flex flex-1">{title}</div>
        )}
      </div>
    </Card>
  );
}
