"use client";
import { Icons } from "@/shared/ui/icons";

import { useQuery } from "@tanstack/react-query";
import {
  adminCrossServiceQueryOptions,
  type AdminUserGrant,
} from "@/entities/admin-cross-service";
import { Badge } from "@/shared/ui/kit/badge";
import { Card, CardContent } from "@/shared/ui/kit/card";
import { EmptyState } from "@/shared/ui/kit/empty-state";
import { Skeleton } from "@/shared/ui/kit/skeleton";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/shared/ui/kit/table";
import { formatDate } from "../../lib/format";
import { RevokeGrantDialog } from "../revoke-grant-dialog";
import { TrialCreditOverrideDialog } from "../trial-credit-override-dialog";

type GrantsTabProps = {
  userId: string;
};

export function GrantsTab({ userId }: GrantsTabProps) {
  const query = useQuery(adminCrossServiceQueryOptions.getGrantsOptions(userId));

  if (query.isLoading) return <Skeletons />;
  if (query.isError) return <EmptyState variant="card" title="Не удалось загрузить grants" icon={Icons.list} />;

  const items = query.data ?? [];
  if (items.length === 0) return <EmptyState variant="card" title="Нет grants" icon={Icons.list} />;

  return (
    <>
      {/* Desktop table */}
      <Card className="hidden md:block">
        <CardContent className="p-0">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>План</TableHead>
                <TableHead>Tier</TableHead>
                <TableHead>Source</TableHead>
                <TableHead>Status</TableHead>
                <TableHead>Выпущен</TableHead>
                <TableHead>Истекает</TableHead>
                <TableHead className="text-right">Действия</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {items.map((g) => (
                <TableRow key={g.id}>
                  <TableCell>{g.planDisplayName ?? <code className="text-xs">{g.planId}</code>}</TableCell>
                  <TableCell>
                    <Badge variant="outline" className="text-xs">
                      {g.planTier ?? "—"}
                    </Badge>
                  </TableCell>
                  <TableCell>
                    <Badge variant="outline" className="text-xs">
                      {g.source}
                    </Badge>
                  </TableCell>
                  <TableCell>
                    <Badge variant={statusVariant(g.status)}>{g.status}</Badge>
                  </TableCell>
                  <TableCell className="text-sm">{formatDate(g.createdAt)}</TableCell>
                  <TableCell className="text-sm">{formatDate(g.expiresAt)}</TableCell>
                  <TableCell className="text-right">
                    {g.status === "ACTIVE" || g.expiresAt != null ? (
                      <div className="flex items-center justify-end gap-1">
                        {/* Time-bounded (месячный) grant — detected by expiresAt != null,
                            НЕ по source (платный месяц = PURCHASE + expiresAt). */}
                        {g.expiresAt != null ? (
                          <TrialCreditOverrideDialog
                            userId={userId}
                            planId={g.planId}
                            planName={grantPlanName(g)}
                          />
                        ) : null}
                        {g.status === "ACTIVE" ? (
                          <RevokeGrantDialog
                            userId={userId}
                            grantId={g.grantId}
                            planName={grantPlanName(g)}
                          />
                        ) : null}
                      </div>
                    ) : (
                      <span className="text-xs text-muted-foreground">—</span>
                    )}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </CardContent>
      </Card>

      {/* Mobile stacked cards */}
      <div className="space-y-3 md:hidden">
        {items.map((g) => (
          <GrantCard key={g.id} grant={g} userId={userId} />
        ))}
      </div>
    </>
  );
}

function GrantCard({ grant, userId }: { grant: AdminUserGrant; userId: string }) {
  return (
    <Card>
      <CardContent className="space-y-3 p-4">
        <div className="flex items-start justify-between gap-2">
          <p className="min-w-0 text-sm font-medium">
            {grant.planDisplayName ?? <code className="text-xs break-all">{grant.planId}</code>}
          </p>
          <Badge variant={statusVariant(grant.status)}>{grant.status}</Badge>
        </div>
        <div className="flex flex-wrap gap-1.5">
          <Badge variant="outline" className="text-xs">
            {grant.planTier ?? "—"}
          </Badge>
          <Badge variant="outline" className="text-xs">
            {grant.source}
          </Badge>
        </div>
        <dl className="grid grid-cols-2 gap-2 text-xs text-muted-foreground">
          <div>
            <dt>Выпущен</dt>
            <dd className="text-foreground/90">{formatDate(grant.createdAt)}</dd>
          </div>
          <div>
            <dt>Истекает</dt>
            <dd className="text-foreground/90">{formatDate(grant.expiresAt)}</dd>
          </div>
        </dl>
        {grant.status === "ACTIVE" || grant.expiresAt != null ? (
          <div className="flex flex-wrap items-center gap-1 border-t border-border/40 pt-2">
            {/* Time-bounded (месячный) grant — detected by expiresAt != null. */}
            {grant.expiresAt != null ? (
              <TrialCreditOverrideDialog
                userId={userId}
                planId={grant.planId}
                planName={grantPlanName(grant)}
              />
            ) : null}
            {grant.status === "ACTIVE" ? (
              <RevokeGrantDialog
                userId={userId}
                grantId={grant.grantId}
                planName={grantPlanName(grant)}
              />
            ) : null}
          </div>
        ) : null}
      </CardContent>
    </Card>
  );
}

function grantPlanName(grant: AdminUserGrant): string {
  return grant.planDisplayName ?? `план ${grant.planId.slice(0, 8)}`;
}

function statusVariant(status: string): "default" | "secondary" | "destructive" | "outline" {
  if (status === "ACTIVE") return "secondary";
  if (status === "REVOKED" || status === "EXPIRED") return "outline";
  return "default";
}

function Skeletons() {
  return (
    <div className="space-y-2">
      {[1, 2, 3].map((i) => (
        <Skeleton key={i} className="h-12 w-full" />
      ))}
    </div>
  );
}
