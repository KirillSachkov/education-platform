"use client";

import { adminCrossServiceQueryOptions } from "@/entities/admin-cross-service";
import { usersAdminApi, type AdminUserSummary, type GetUsersParams } from "@/entities/user";
import { getErrorMessage } from "@/shared/api";
import { apiClient } from "@/shared/api/axios-instance";
import { Button } from "@/shared/ui/kit/button";
import { Checkbox } from "@/shared/ui/kit/checkbox";
import {
  Table,
  TableBody,
  TableHead,
  TableHeader,
  TableRow,
} from "@/shared/ui/kit/table";
import { useQuery } from "@tanstack/react-query";
import { ChevronLeft, ChevronRight, Download, Loader2, Plus } from "lucide-react";
import { useEffect, useState } from "react";
import { useAdminUsers } from "../model/use-admin-users";
import { useBulkLockout } from "../model/use-bulk-lockout";
import { BulkActionToolbar } from "./bulk-action-toolbar";
import { CreateUserDialog } from "./create-user-dialog";
import { DeleteUserDialog } from "./delete-user-dialog";
import { EditUserDialog } from "./edit-user-dialog";
import { ManageRolesDialog } from "./manage-roles-dialog";
import { SetPasswordDialog } from "./set-password-dialog";
import { UserCard } from "./user-card";
import { UserRow } from "./user-row";
import { UsersFilterBar } from "./users-filter-bar";

type StatusValue = "" | "active" | "locked" | "unconfirmed";

function useDebouncedValue<T>(value: T, delay: number): T {
  const [debounced, setDebounced] = useState(value);

  useEffect(() => {
    const timer = setTimeout(() => setDebounced(value), delay);
    return () => clearTimeout(timer);
  }, [value, delay]);

  return debounced;
}

function toIsoStart(date: string): string | undefined {
  if (!date) return undefined;
  return `${date}T00:00:00.000Z`;
}

function toIsoEnd(date: string): string | undefined {
  if (!date) return undefined;
  return `${date}T23:59:59.999Z`;
}

export function AdminUsersPage() {
  const [page, setPage] = useState(1);
  const [search, setSearch] = useState("");
  const [roleFilter, setRoleFilter] = useState<string>("");
  const [statusFilter, setStatusFilter] = useState<StatusValue>("");
  const [createdAfter, setCreatedAfter] = useState("");
  const [createdBefore, setCreatedBefore] = useState("");
  const debouncedSearch = useDebouncedValue(search, 300);

  const filterParams: GetUsersParams = {
    page,
    pageSize: 20,
    search: debouncedSearch || undefined,
    role: roleFilter || undefined,
    status: statusFilter || undefined,
    createdAfter: toIsoStart(createdAfter),
    createdBefore: toIsoEnd(createdBefore),
  };

  const { data, isLoading, error } = useAdminUsers(filterParams);
  const bulkLockout = useBulkLockout();

  const [createOpen, setCreateOpen] = useState(false);
  const [editUser, setEditUser] = useState<AdminUserSummary | null>(null);
  const [passwordUser, setPasswordUser] = useState<AdminUserSummary | null>(null);
  const [rolesUser, setRolesUser] = useState<AdminUserSummary | null>(null);
  const [deleteUser, setDeleteUser] = useState<AdminUserSummary | null>(null);
  const [selected, setSelected] = useState<Set<string>>(new Set());

  const users = data?.items ?? [];
  const totalPages = data?.totalPages ?? 0;

  const userIds = users.map((u) => u.id);
  const grantsQuery = useQuery(
    adminCrossServiceQueryOptions.getActiveGrantsByUsersOptions(userIds),
  );
  const grantsByUser = grantsQuery.data ?? {};

  // Selection persists across pages — counted total includes IDs from previous pages.
  const allSelectedOnPage =
    users.length > 0 && users.every((u) => selected.has(u.id));

  const toggleSelect = (userId: string, checked: boolean) => {
    setSelected((prev) => {
      const next = new Set(prev);
      if (checked) next.add(userId);
      else next.delete(userId);
      return next;
    });
  };

  const toggleSelectAll = (checked: boolean) => {
    setSelected((prev) => {
      const next = new Set(prev);
      users.forEach((u) => {
        if (checked) next.add(u.id);
        else next.delete(u.id);
      });
      return next;
    });
  };

  const handleSearchChange = (value: string) => {
    setSearch(value);
    setPage(1);
  };

  const handleRoleFilter = (value: string) => {
    setRoleFilter(value);
    setPage(1);
  };

  const handleStatusFilter = (value: StatusValue) => {
    setStatusFilter(value);
    setPage(1);
  };

  const handleCreatedAfter = (value: string) => {
    setCreatedAfter(value);
    setPage(1);
  };

  const handleCreatedBefore = (value: string) => {
    setCreatedBefore(value);
    setPage(1);
  };

  const handleExportCsv = async () => {
    try {
      const url = usersAdminApi.buildExportCsvUrl({
        search: debouncedSearch || undefined,
        role: roleFilter || undefined,
        status: statusFilter || undefined,
        createdAfter: toIsoStart(createdAfter),
        createdBefore: toIsoEnd(createdBefore),
      });
      const response = await apiClient.get<Blob>(url, { responseType: "blob" });
      const blobUrl = window.URL.createObjectURL(response.data);
      const a = document.createElement("a");
      a.href = blobUrl;
      a.download = `users-${new Date().toISOString().slice(0, 10)}.csv`;
      document.body.appendChild(a);
      a.click();
      a.remove();
      window.URL.revokeObjectURL(blobUrl);
    } catch (err) {
      console.error("CSV export failed", err);
    }
  };

  const runBulkLockout = (isLocked: boolean) => {
    if (selected.size === 0) return;
    bulkLockout.mutate(
      { userIds: Array.from(selected), isLocked },
      { onSuccess: () => setSelected(new Set()) },
    );
  };

  return (
    // overflow-x-clip: mobile horizontal-overflow guard (#446). The stacked card
    // list + filter bar are responsive, but this keeps any single wide cell (long
    // plan name, email, badge row) from pushing the whole page sideways on phones.
    // No-op on desktop where the table lives in its own overflow-x-auto wrapper.
    <div className="max-w-6xl mx-auto p-4 md:p-6 overflow-x-clip">
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3 mb-6">
        <div>
          <h1 className="text-xl font-bold">Управление пользователями</h1>
          <p className="text-sm text-muted-foreground">
            {data?.totalCount ?? 0} пользователей
          </p>
        </div>
        <div className="flex flex-col gap-2 sm:flex-row">
          <Button
            variant="outline"
            className="w-full sm:w-auto"
            onClick={handleExportCsv}
          >
            <Download size={15} /> Экспорт CSV
          </Button>
          <Button
            className="bg-gradient-primary text-primary-foreground border-0 hover:opacity-90 w-full sm:w-auto"
            onClick={() => setCreateOpen(true)}
          >
            <Plus size={15} /> Создать пользователя
          </Button>
        </div>
      </div>

      <UsersFilterBar
        search={search}
        onSearchChange={handleSearchChange}
        roleFilter={roleFilter}
        onRoleFilterChange={handleRoleFilter}
        statusFilter={statusFilter}
        onStatusFilterChange={handleStatusFilter}
        createdAfter={createdAfter}
        onCreatedAfterChange={handleCreatedAfter}
        createdBefore={createdBefore}
        onCreatedBeforeChange={handleCreatedBefore}
      />

      <BulkActionToolbar
        count={selected.size}
        onClear={() => setSelected(new Set())}
        onBulkLock={() => runBulkLockout(true)}
        onBulkUnlock={() => runBulkLockout(false)}
        isPending={bulkLockout.isPending}
      />

      {isLoading ? (
        <div className="flex justify-center py-12">
          <Loader2 className="h-6 w-6 animate-spin text-muted-foreground" />
        </div>
      ) : error ? (
        <div className="text-center py-12">
          <p className="text-sm text-destructive">
            {getErrorMessage(error, "Ошибка загрузки пользователей")}
          </p>
        </div>
      ) : users.length === 0 ? (
        <div className="text-center py-12 text-muted-foreground">
          Пользователи не найдены
        </div>
      ) : (
        <>
          {/* Mobile: stacked cards from the same users array */}
          <div className="flex flex-col gap-3 md:hidden">
            {users.map((user) => (
              <UserCard
                key={user.id}
                user={user}
                isSelected={selected.has(user.id)}
                grants={grantsByUser[user.id]}
                isGrantsLoading={grantsQuery.isLoading}
                onSelectChange={(checked) => toggleSelect(user.id, checked)}
                onEdit={setEditUser}
                onSetPassword={setPasswordUser}
                onManageRoles={setRolesUser}
                onDelete={setDeleteUser}
              />
            ))}
          </div>

          {/* Desktop: full table */}
          <div className="hidden rounded-md border md:block">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead className="w-10">
                    <Checkbox
                      checked={allSelectedOnPage}
                      onCheckedChange={(c) => toggleSelectAll(Boolean(c))}
                      aria-label="Выбрать всех на странице"
                    />
                  </TableHead>
                  <TableHead>Пользователь</TableHead>
                  <TableHead>Роли</TableHead>
                  <TableHead>Активные планы</TableHead>
                  <TableHead>Статус</TableHead>
                  <TableHead>Дата создания</TableHead>
                  <TableHead className="text-right">Действия</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {users.map((user) => (
                  <UserRow
                    key={user.id}
                    user={user}
                    isSelected={selected.has(user.id)}
                    grants={grantsByUser[user.id]}
                    isGrantsLoading={grantsQuery.isLoading}
                    onSelectChange={(checked) => toggleSelect(user.id, checked)}
                    onEdit={setEditUser}
                    onSetPassword={setPasswordUser}
                    onManageRoles={setRolesUser}
                    onDelete={setDeleteUser}
                  />
                ))}
              </TableBody>
            </Table>
          </div>

          {totalPages > 1 && (
            <div className="flex items-center justify-center gap-2 mt-4">
              <Button
                variant="outline"
                size="sm"
                className="min-touch"
                onClick={() => setPage((p) => Math.max(1, p - 1))}
                disabled={page <= 1}
                aria-label="Предыдущая страница"
              >
                <ChevronLeft size={14} />
              </Button>
              <span className="text-sm text-muted-foreground">
                {page} / {totalPages}
              </span>
              <Button
                variant="outline"
                size="sm"
                className="min-touch"
                onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
                disabled={page >= totalPages}
                aria-label="Следующая страница"
              >
                <ChevronRight size={14} />
              </Button>
            </div>
          )}
        </>
      )}

      <CreateUserDialog open={createOpen} onOpenChange={setCreateOpen} />

      {editUser && (
        <EditUserDialog
          user={editUser}
          open={!!editUser}
          onOpenChange={(open) => !open && setEditUser(null)}
        />
      )}

      {passwordUser && (
        <SetPasswordDialog
          userId={passwordUser.id}
          userEmail={passwordUser.email}
          open={!!passwordUser}
          onOpenChange={(open) => !open && setPasswordUser(null)}
        />
      )}

      {rolesUser && (
        <ManageRolesDialog
          userId={rolesUser.id}
          userEmail={rolesUser.email}
          currentRoles={rolesUser.roles}
          open={!!rolesUser}
          onOpenChange={(open) => !open && setRolesUser(null)}
        />
      )}

      {deleteUser && (
        <DeleteUserDialog
          userId={deleteUser.id}
          userEmail={deleteUser.email}
          open={!!deleteUser}
          onOpenChange={(open) => !open && setDeleteUser(null)}
        />
      )}
    </div>
  );
}
