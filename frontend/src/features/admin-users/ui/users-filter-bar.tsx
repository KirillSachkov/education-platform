"use client";

import { PLATFORM_ROLES } from "@/shared/config/roles";
import { DatePicker } from "@/shared/ui/kit/date-picker";
import { Input } from "@/shared/ui/kit/input";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/shared/ui/kit/select";

const isFutureDate = (date: Date) => date.getTime() > Date.now();

type StatusValue = "" | "active" | "locked" | "unconfirmed";

type Props = {
  search: string;
  onSearchChange: (value: string) => void;
  roleFilter: string;
  onRoleFilterChange: (value: string) => void;
  statusFilter: StatusValue;
  onStatusFilterChange: (value: StatusValue) => void;
  createdAfter: string;
  onCreatedAfterChange: (value: string) => void;
  createdBefore: string;
  onCreatedBeforeChange: (value: string) => void;
};

export function UsersFilterBar({
  search,
  onSearchChange,
  roleFilter,
  onRoleFilterChange,
  statusFilter,
  onStatusFilterChange,
  createdAfter,
  onCreatedAfterChange,
  createdBefore,
  onCreatedBeforeChange,
}: Props) {
  return (
    <div className="flex flex-col gap-2 sm:flex-row sm:flex-wrap sm:items-center sm:gap-3 mb-4">
      <Input
        placeholder="Поиск: имя, username, Telegram…"
        value={search}
        onChange={(e) => onSearchChange(e.target.value)}
        className="w-full sm:max-w-sm"
      />
      <Select
        value={roleFilter}
        onValueChange={(v) => onRoleFilterChange(v === "all" ? "" : v)}
      >
        <SelectTrigger className="w-full sm:w-[180px]">
          <SelectValue placeholder="Все роли" />
        </SelectTrigger>
        <SelectContent>
          <SelectItem value="all">Все роли</SelectItem>
          {PLATFORM_ROLES.map((r) => (
            <SelectItem key={r.value} value={r.value}>
              {r.label}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>
      <Select
        value={statusFilter || "all"}
        onValueChange={(v) => onStatusFilterChange(v === "all" ? "" : (v as StatusValue))}
      >
        <SelectTrigger className="w-full sm:w-[180px]">
          <SelectValue placeholder="Любой статус" />
        </SelectTrigger>
        <SelectContent>
          <SelectItem value="all">Любой статус</SelectItem>
          <SelectItem value="active">Активные</SelectItem>
          <SelectItem value="locked">Залочены</SelectItem>
          <SelectItem value="unconfirmed">Без email-confirm</SelectItem>
        </SelectContent>
      </Select>
      <div className="flex flex-col gap-2 text-xs text-muted-foreground sm:flex-row sm:items-center sm:gap-2">
        <div className="flex items-center gap-2">
          <span className="w-16 shrink-0 sm:w-auto">Зарег. с</span>
          <DatePicker
            value={createdAfter || undefined}
            onChange={(value) => onCreatedAfterChange(value ?? "")}
            disabledDates={isFutureDate}
            buttonClassName="w-full sm:w-auto"
          />
        </div>
        <div className="flex items-center gap-2">
          <span className="w-16 shrink-0 sm:w-auto">по</span>
          <DatePicker
            value={createdBefore || undefined}
            onChange={(value) => onCreatedBeforeChange(value ?? "")}
            disabledDates={isFutureDate}
            buttonClassName="w-full sm:w-auto"
          />
        </div>
      </div>
    </div>
  );
}
