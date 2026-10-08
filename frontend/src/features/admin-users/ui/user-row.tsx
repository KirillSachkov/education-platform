"use client";

import type { AdminActiveGrantSummary } from "@/entities/admin-cross-service";
import type { AdminUserSummary } from "@/entities/user";
import { routes } from "@/shared/config/routes";
import { PLATFORM_ROLES } from "@/shared/config/roles";
import { formatShortDateWithTime } from "@/shared/lib/date/format";
import { UserAvatar } from "@/shared/ui/components";
import { Badge } from "@/shared/ui/kit/badge";
import { Button } from "@/shared/ui/kit/button";
import { Checkbox } from "@/shared/ui/kit/checkbox";
import { TableCell, TableRow } from "@/shared/ui/kit/table";
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

export function UserRow({
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
    <TableRow>
      <TableCell className="w-10">
        <Checkbox
          checked={isSelected}
          onCheckedChange={(c) => onSelectChange(Boolean(c))}
          aria-label="Выбрать пользователя"
        />
      </TableCell>
      <TableCell>
        <Link
          href={routes.adminUserDetail(user.id)}
          className="flex items-center gap-2.5 hover:underline"
        >
          <UserAvatar
            name={user.displayName ?? user.userName}
            avatarId={user.avatarId}
            className="size-8 shrink-0"
          />
          <div>
            <p className="font-medium text-sm">{user.userName}</p>
            <p className="text-xs text-muted-foreground">{user.email}</p>
          </div>
        </Link>
      </TableCell>
      <TableCell>
        <div className="flex flex-wrap gap-1">
          {user.roles.map((role) => (
            <Badge key={role} variant="secondary" className="text-[11px]">
              {roleLabels[role] ?? role}
            </Badge>
          ))}
        </div>
      </TableCell>
      <TableCell>
        <UserPlansCell grants={grants} isLoading={isGrantsLoading} />
      </TableCell>
      <TableCell>
        <UserStatusBadge user={user} />
      </TableCell>
      <TableCell className="text-sm text-muted-foreground">
        {formatShortDateWithTime(user.createdAt)}
      </TableCell>
      <TableCell className="text-right">
        <div className="flex items-center justify-end gap-1">
          <Button
            variant="ghost"
            size="sm"
            className="text-xs"
            onClick={() => onEdit(user)}
            aria-label="Редактировать"
          >
            <Pencil size={13} />
          </Button>
          <Button
            variant="ghost"
            size="sm"
            className="text-xs"
            onClick={() => onSetPassword(user)}
            aria-label="Сменить пароль"
          >
            <Key size={13} />
          </Button>
          <Button
            variant="ghost"
            size="sm"
            className="text-xs"
            onClick={() => onManageRoles(user)}
            aria-label="Управление ролями"
          >
            <Shield size={13} />
          </Button>
          <LockoutToggle userId={user.id} isLockedOut={user.isLockedOut} />
          <Button
            variant="ghost"
            size="sm"
            className="text-xs text-destructive hover:text-destructive"
            onClick={() => onDelete(user)}
            aria-label="Удалить"
          >
            <Trash2 size={13} />
          </Button>
        </div>
      </TableCell>
    </TableRow>
  );
}
