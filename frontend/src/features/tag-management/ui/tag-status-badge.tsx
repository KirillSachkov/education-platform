import type { TagKind } from "@/entities/tag";
import { Badge } from "@/shared/ui/kit/badge";
import { cn } from "@/shared/lib/css";

const kindStyles: Record<TagKind, string> = {
  canon: "border-teal/30 bg-teal/10 text-teal",
  alias: "border-blue/30 bg-blue/10 text-blue",
};

const kindLabels: Record<TagKind, string> = {
  canon: "Каноничный",
  alias: "Алиас",
};

interface TagStatusBadgeProps {
  kind: TagKind;
  className?: string;
}

export function TagStatusBadge({ kind, className }: TagStatusBadgeProps) {
  return (
    <Badge
      variant="outline"
      className={cn(kindStyles[kind], className)}
    >
      {kindLabels[kind]}
    </Badge>
  );
}
