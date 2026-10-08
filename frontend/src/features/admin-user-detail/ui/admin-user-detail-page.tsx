"use client";

import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import type { ReactNode } from "react";
import { usersQueryOptions } from "@/entities/user";
import { routes } from "@/shared/config/routes";
import { Button } from "@/shared/ui/kit/button";
import { Card, CardContent } from "@/shared/ui/kit/card";
import { EmptyState } from "@/shared/ui/kit/empty-state";
import { Icons } from "@/shared/ui/icons";
import { Skeleton } from "@/shared/ui/kit/skeleton";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/shared/ui/kit/tabs";
import { AuditLogTab } from "./tabs/audit-log-tab";
import { CommentsTab } from "./tabs/comments-tab";
import { EnrollmentsTab } from "./tabs/enrollments-tab";
import { GrantsTab } from "./tabs/grants-tab";
import { PaymentsTab } from "./tabs/payments-tab";
import { ProfileTab } from "./tabs/profile-tab";
import { SubmissionsTab } from "./tabs/submissions-tab";

type AdminUserDetailPageProps = {
  userId: string;
  /**
   * Контент вкладки «Доступы и сообщества» (#444). Приходит слотом из app-роута,
   * т.к. `features/admin-access-communities` — соседний slice (cross-feature
   * import запрещён FSD-границами). Опционален для обратной совместимости.
   */
  accessCommunitiesSlot?: ReactNode;
};

export function AdminUserDetailPage({ userId, accessCommunitiesSlot }: AdminUserDetailPageProps) {
  const query = useQuery(usersQueryOptions.getUserDetailOptions(userId));

  if (query.isLoading) {
    return (
      <div className="container mx-auto max-w-6xl space-y-6 p-6">
        <Skeleton className="h-10 w-64" />
        <Skeleton className="h-64 w-full" />
      </div>
    );
  }

  if (query.isError || !query.data) {
    return (
      <div className="container mx-auto max-w-6xl p-6">
        <EmptyState variant="card" title="Пользователь не найден" icon={Icons.error} />
        <div className="mt-4">
          <Button asChild variant="outline">
            <Link href={routes.adminUsers}>
              <Icons.arrowLeft className="mr-2 h-4 w-4" />
              К списку
            </Link>
          </Button>
        </div>
      </div>
    );
  }

  const user = query.data;
  const displayName = user.displayName ?? user.userName ?? user.email ?? user.id;

  return (
    <div className="container mx-auto max-w-6xl space-y-6 p-6">
      <div className="flex items-start justify-between gap-4">
        <div className="space-y-1">
          <Button asChild variant="ghost" size="sm">
            <Link href={routes.adminUsers}>
              <Icons.arrowLeft className="mr-1 h-4 w-4" />
              К списку
            </Link>
          </Button>
          <h1 className="text-2xl font-semibold">{displayName}</h1>
          <p className="text-sm text-muted-foreground">{user.email ?? "—"}</p>
        </div>
      </div>

      <Card>
        <CardContent className="p-4 grid grid-cols-2 gap-3 text-sm md:grid-cols-4">
          <Stat label="Создан" value={new Date(user.createdAt).toLocaleDateString("ru")} />
          <Stat
            label="Последний логин"
            value={user.lastLoginAt ? new Date(user.lastLoginAt).toLocaleDateString("ru") : "—"}
          />
          <Stat label="Email" value={user.emailConfirmed ? "подтверждён" : "не подтверждён"} />
          <Stat label="Статус" value={user.isLockedOut ? "залочен" : "активен"} />
        </CardContent>
      </Card>

      <Tabs defaultValue="profile">
        <TabsList className="flex w-full flex-wrap gap-1">
          <TabsTrigger value="profile">Профиль</TabsTrigger>
          <TabsTrigger value="enrollments">Курсы</TabsTrigger>
          <TabsTrigger value="submissions">Сабмишены</TabsTrigger>
          <TabsTrigger value="grants">Grants</TabsTrigger>
          <TabsTrigger value="payments">Платежи</TabsTrigger>
          {accessCommunitiesSlot ? (
            <TabsTrigger value="access-communities">Доступы и сообщества</TabsTrigger>
          ) : null}
          <TabsTrigger value="comments">Комментарии</TabsTrigger>
          <TabsTrigger value="audit">Admin log</TabsTrigger>
        </TabsList>

        <TabsContent value="profile" className="mt-4">
          <ProfileTab user={user} />
        </TabsContent>
        <TabsContent value="enrollments" className="mt-4">
          <EnrollmentsTab userId={userId} />
        </TabsContent>
        <TabsContent value="submissions" className="mt-4">
          <SubmissionsTab userId={userId} />
        </TabsContent>
        <TabsContent value="grants" className="mt-4">
          <GrantsTab userId={userId} />
        </TabsContent>
        <TabsContent value="payments" className="mt-4">
          <PaymentsTab userId={userId} />
        </TabsContent>
        {accessCommunitiesSlot ? (
          <TabsContent value="access-communities" className="mt-4">
            {accessCommunitiesSlot}
          </TabsContent>
        ) : null}
        <TabsContent value="comments" className="mt-4">
          <CommentsTab userId={userId} />
        </TabsContent>
        <TabsContent value="audit" className="mt-4">
          <AuditLogTab userId={userId} />
        </TabsContent>
      </Tabs>
    </div>
  );
}

function Stat({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <div className="text-xs text-muted-foreground">{label}</div>
      <div className="mt-0.5 font-medium">{value}</div>
    </div>
  );
}
