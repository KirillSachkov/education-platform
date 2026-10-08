"use client";

import type { AdminUserSummary } from "@/entities/user";
import { Badge } from "@/shared/ui/kit/badge";

type Props = {
  user: AdminUserSummary;
};

export function UserStatusBadge({ user }: Props) {
  if (user.isLockedOut) {
    return (
      <Badge
        variant="outline"
        className="border-destructive/30 bg-destructive/10 text-destructive text-[11px]"
      >
        Заблокирован
      </Badge>
    );
  }

  if (!user.emailConfirmed) {
    return (
      <Badge
        variant="outline"
        className="border-yellow/30 bg-yellow/10 text-yellow text-[11px]"
      >
        Email не подтверждён
      </Badge>
    );
  }

  return (
    <Badge
      variant="outline"
      className="border-teal/30 bg-teal/10 text-teal text-[11px]"
    >
      Активен
    </Badge>
  );
}
