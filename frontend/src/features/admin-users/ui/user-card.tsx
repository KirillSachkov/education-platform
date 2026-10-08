"use client";

import type { AdminActiveGrantSummary } from "@/entities/admin-cross-service";
import type { AdminUserSummary } from "@/entities/user";
import { routes } from "@/shared/config/routes";
import { PLATFORM_ROLES } from "@/shared/config/roles";
import { formatShortDateWithTime } from "@/shared/lib/date/format";
import { UserAvatar } from "@/shared/ui/components";
import { Badge } from "@/shared/ui/kit/badge";
import { Button } from "@/shared/ui/kit/button";
import { Card, CardContent } from "@/shared/ui/kit/card";
import { Checkbox } from "@/shared/ui/kit/checkbox";
import { Key, Pencil, Shield, Trash2 } from "lucide-react";
import Link from "next/link";
import { LockoutToggle } from "./lockout-toggle";
import { UserPlansCell } from "./user-plans-cell";
import { UserStatusBadge } from "./user-status-badge";

const roleLabels: Record<string, string> = Object.fromEntries(
  PLATFORM_ROLES.map((r) => [r.value, r.label]),
);

type Props = {
  user: AdminUserSummary;
  isSelected: boolean;
  grants: AdminActiveGrantSummary[] | undefined;
  isGrantsLoading: boolean;
  onSelectChange: (checked: boolean) => void;
  onEdit: (user: AdminUserSummary) => void;
  onSetPassword: (user: AdminUserSummary) => void;
  onManageRoles: (user: AdminUserSummary) => void;
  onDelete: (user: AdminUserSummary) => void;
};

export function UserCard({
  user,
  isSelected,
  grants,
  isGrantsLoading,
  onSelectChange,
  onEdit,
  onSetPassword,
  onManageRoles,
  onDelete,
}: Props) {
  return (
    <Card className="gap-0 py-0">
      <CardContent className="flex flex-col gap-3 p-4">
        <div className="flex items-start gap-3">
          <Checkbox
            checked={isSelected}
            onCheckedChange={(c) => onSelectChange(Boolean(c))}
            aria-label="Выбрать пользователя"
            className="mt-1 shrink-0"
          />
          <Link
            href={routes.adminUserDetail(user.id)}
            className="flex min-w-0 flex-1 items-center gap-2.5 hover:underline"
          >
            <UserAvatar
              name={user.displayName ?? user.userName}
              avatarId={user.avatarId}
              className="size-9 shrink-0"
            />
            <div className="min-w-0">
              <p className="truncate text-sm font-medium">{user.userName}</p>
              <p className="truncate text-xs text-muted-foreground">{user.email}</p>
            </div>
          </Link>
          <UserStatusBadge user={user} />
        </div>

        {user.roles.length > 0 ? (
          <div className="flex flex-wrap gap-1">
            {user.roles.map((role) => (
              <Badge key={role} variant="secondary" className="text-[11px]">
                {roleLabels[role] ?? role}
              </Badge>
            ))}
          </div>
        ) : null}

        <div className="flex flex-wrap items-center gap-x-2 gap-y-1 text-xs text-muted-foreground">
          <span className="shrink-0">Планы:</span>
          <UserPlansCell grants={grants} isLoading={isGrantsLoading} />
        </div>

        <p className="text-xs text-muted-foreground">
          Создан {formatShortDateWithTime(user.createdAt)}
        </p>

        {/* Icon-only action row — keeps all five controls on a single line at any
            mobile width (no wrap/collision). Labels surface via aria-label + native
            tooltip. Desktop uses the labelled buttons in user-row.tsx. */}
        <div className="grid grid-cols-5 items-center gap-1.5 border-t pt-3">
          <Button
            variant="outline"
            size="icon"
            className="min-touch w-full"
            aria-label="Изменить"
            title="Изменить"
            onClick={() => onEdit(user)}
          >
            <Pencil size={16} />
          </Button>
          <Button
            variant="outline"
            size="icon"
            className="min-touch w-full"
            aria-label="Сбросить пароль"
            title="Сбросить пароль"
            onClick={() => onSetPassword(user)}
          >
            <Key size={16} />
          </Button>
          <Button
            variant="outline"
            size="icon"
            className="min-touch w-full"
            aria-label="Роли"
            title="Роли"
            onClick={() => onManageRoles(user)}
          >
            <Shield size={16} />
          </Button>
          <LockoutToggle userId={user.id} isLockedOut={user.isLockedOut} iconOnly />
          <Button
            variant="outline"
            size="icon"
            className="min-touch w-full text-destructive hover:text-destructive"
            aria-label="Удалить"
            title="Удалить"
            onClick={() => onDelete(user)}
          >
            <Trash2 size={16} />
          </Button>
        </div>
      </CardContent>
    </Card>
  );
}
