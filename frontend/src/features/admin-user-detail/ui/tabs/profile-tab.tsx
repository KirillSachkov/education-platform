"use client";

import type { AdminUserDetail } from "@/entities/user";
import { Icons } from "@/shared/ui/icons";
import { Badge } from "@/shared/ui/kit/badge";
import { Card, CardContent } from "@/shared/ui/kit/card";
import { formatDate } from "../../lib/format";

type ProfileTabProps = {
  user: AdminUserDetail;
};

export function ProfileTab({ user }: ProfileTabProps) {
  const telegram = user.telegramUsername?.replace(/^@+/, "").trim() || null;

  return (
    <Card>
      <CardContent className="p-4 sm:p-6">
        <dl className="divide-y divide-border/50">
          <Row label="ID">
            <code className="text-xs break-all">{user.id}</code>
          </Row>
          <Row label="Username">{user.userName ?? "—"}</Row>
          <Row label="Display name">{user.displayName ?? "—"}</Row>
          <Row label="Email">
            {user.email ? (
              <span className="inline-flex flex-wrap items-center gap-x-2 gap-y-1">
                <a
                  href={`mailto:${user.email}`}
                  className="inline-flex items-center gap-1.5 text-teal hover:underline break-all"
                >
                  <Icons.mail size={14} className="shrink-0" />
                  {user.email}
                </a>
                {user.emailConfirmed ? (
                  <Badge variant="secondary" className="text-xs">
                    подтверждён
                  </Badge>
                ) : (
                  <Badge variant="outline" className="text-xs">
                    не подтверждён
                  </Badge>
                )}
              </span>
            ) : (
              "—"
            )}
          </Row>
          <Row label="Telegram">
            {telegram ? (
              <a
                href={`https://t.me/${telegram}`}
                target="_blank"
                rel="noopener noreferrer"
                className="inline-flex items-center gap-1.5 text-teal hover:underline"
              >
                <Icons.telegram size={14} className="shrink-0" />@{telegram}
              </a>
            ) : (
              <span className="text-muted-foreground">не привязан</span>
            )}
          </Row>
          <Row label="Роли">
            <div className="flex flex-wrap gap-1.5">
              {user.roles.length === 0 ? (
                <span className="text-muted-foreground">—</span>
              ) : (
                user.roles.map((r) => (
                  <Badge key={r} variant="secondary" className="text-xs">
                    {r}
                  </Badge>
                ))
              )}
            </div>
          </Row>
          <Row label="Статус">
            {user.isLockedOut ? (
              <Badge variant="destructive">Залочен</Badge>
            ) : (
              <Badge variant="secondary">Активен</Badge>
            )}
          </Row>
          <Row label="Создан">{formatDate(user.createdAt)}</Row>
          <Row label="Обновлён">{formatDate(user.updatedAt)}</Row>
          <Row label="Последний логин">{formatDate(user.lastLoginAt)}</Row>
          <Row label="Lockout до">{formatDate(user.lockoutEnd)}</Row>
          {user.bio ? <Row label="Bio">{user.bio}</Row> : null}
        </dl>
      </CardContent>
    </Card>
  );
}

/**
 * Mobile-first строка «лейбл / значение» (#575). На ≤375px лейбл и значение стекаются
 * (лейбл — мелкий капс над значением), на sm+ — две колонки. До этого фиксированный
 * `grid-cols-[180px_1fr]` сжимал значение в кашу на узких экранах.
 */
function Row({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="grid grid-cols-1 gap-0.5 py-3 first:pt-0 last:pb-0 sm:grid-cols-[160px_1fr] sm:items-baseline sm:gap-4">
      <dt className="text-[11px] font-semibold uppercase tracking-wide text-muted-foreground sm:text-sm sm:font-normal sm:normal-case sm:tracking-normal">
        {label}
      </dt>
      <dd className="min-w-0 text-sm break-words">{children}</dd>
    </div>
  );
}
