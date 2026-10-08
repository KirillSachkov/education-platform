"use client";

import { usersQueryOptions, type AdminAuditLogQuery } from "@/entities/user";
import { Badge } from "@/shared/ui/kit/badge";
import { Button } from "@/shared/ui/kit/button";
import { Card, CardContent } from "@/shared/ui/kit/card";
import { DatePicker } from "@/shared/ui/kit/date-picker";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/shared/ui/kit/select";
import { Skeleton } from "@/shared/ui/kit/skeleton";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/shared/ui/kit/table";
import { useQuery } from "@tanstack/react-query";
import { ChevronRight } from "lucide-react";
import { useState } from "react";

const ACTIONS = [
  "users.created",
  "users.updated",
  "users.deleted",
  "users.lockout.set",
  "users.password.set",
  "users.roles.set",
  "users.bulk.lockout",
  "users.bulk.roles",
  "users.export.csv",
];

const dateFormatter = new Intl.DateTimeFormat("ru", {
  dateStyle: "medium",
  timeStyle: "short",
});

function toIsoStart(date: string): string | undefined {
  if (!date) return undefined;
  return `${date}T00:00:00.000Z`;
}

function toIsoEnd(date: string): string | undefined {
  if (!date) return undefined;
  return `${date}T23:59:59.999Z`;
}

export function AdminAuditLogPage() {
  const [actionFilter, setActionFilter] = useState("");
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [cursor, setCursor] = useState<string | undefined>(undefined);

  const query: AdminAuditLogQuery = {
    action: actionFilter || undefined,
    from: toIsoStart(from),
    to: toIsoEnd(to),
    cursor,
    pageSize: 50,
  };

  const result = useQuery(usersQueryOptions.getAdminAuditLogOptions(query));

  const items = result.data?.items ?? [];
  const nextCursor = result.data?.nextCursor ?? null;

  const reset = (mutator: () => void) => {
    setCursor(undefined);
    mutator();
  };

  return (
    <div className="container mx-auto max-w-6xl space-y-6 p-4 md:p-6">
      <div>
        <h1 className="text-2xl font-semibold">Audit log</h1>
        <p className="text-sm text-muted-foreground">
          Все admin-мутации (POST/PATCH/DELETE) на эндпоинтах `/users/*` — для compliance и дебага.
        </p>
      </div>

      <Card>
        <CardContent className="flex flex-col gap-3 p-4 sm:flex-row sm:flex-wrap sm:items-end">
          <div className="space-y-1">
            <label className="block text-xs text-muted-foreground">Действие</label>
            <Select
              value={actionFilter || "all"}
              onValueChange={(v) => reset(() => setActionFilter(v === "all" ? "" : v))}
            >
              <SelectTrigger className="w-full sm:w-[220px]">
                <SelectValue placeholder="Все действия" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="all">Все действия</SelectItem>
                {ACTIONS.map((a) => (
                  <SelectItem key={a} value={a}>
                    {a}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <div className="space-y-1">
            <label className="block text-xs text-muted-foreground">С</label>
            <DatePicker
              value={from || undefined}
              onChange={(value) => reset(() => setFrom(value ?? ""))}
              buttonClassName="w-full sm:w-[160px]"
            />
          </div>
          <div className="space-y-1">
            <label className="block text-xs text-muted-foreground">По</label>
            <DatePicker
              value={to || undefined}
              onChange={(value) => reset(() => setTo(value ?? ""))}
              buttonClassName="w-full sm:w-[160px]"
            />
          </div>
        </CardContent>
      </Card>

      <Card>
        <CardContent className="p-0">
          {result.isLoading ? (
            <div className="space-y-2 p-4">
              {[1, 2, 3, 4, 5].map((i) => (
                <Skeleton key={i} className="h-10 w-full" />
              ))}
            </div>
          ) : items.length === 0 ? (
            <div className="p-8 text-center text-sm text-muted-foreground">Записей нет</div>
          ) : (
            <>
              {/* Mobile: stacked cards from the same items array */}
              <ul className="divide-y md:hidden" aria-label="Журнал аудита">
                {items.map((entry) => (
                  <li key={entry.id} className="flex flex-col gap-2 p-4">
                    <div className="flex flex-wrap items-center gap-2">
                      <Badge variant="outline" className="text-xs">
                        {entry.action}
                      </Badge>
                      {entry.result === "success" ? (
                        <Badge variant="secondary">success</Badge>
                      ) : (
                        <Badge variant="destructive" title={entry.errorMessage ?? undefined}>
                          failure
                        </Badge>
                      )}
                      <span className="font-mono text-xs text-muted-foreground">
                        {entry.method}
                      </span>
                    </div>
                    <div className="text-xs text-muted-foreground">
                      {dateFormatter.format(new Date(entry.createdAt))}
                    </div>
                    <div className="text-sm">
                      <span className="text-muted-foreground">Админ: </span>
                      {entry.adminUsername ?? entry.adminDisplayName ?? (
                        <code className="text-xs">{entry.adminId}</code>
                      )}
                    </div>
                    <div className="text-sm break-all">
                      <span className="text-muted-foreground">Цель: </span>
                      {entry.targetUserId ? (
                        <code className="text-xs">{entry.targetUserId}</code>
                      ) : (
                        "—"
                      )}
                    </div>
                    {entry.ipAddress ? (
                      <div className="text-xs text-muted-foreground">
                        IP: {entry.ipAddress}
                      </div>
                    ) : null}
                  </li>
                ))}
              </ul>

              {/* Desktop: full table */}
              <div className="hidden md:block">
                <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead>Когда</TableHead>
                      <TableHead>Админ</TableHead>
                      <TableHead>Действие</TableHead>
                      <TableHead>Цель</TableHead>
                      <TableHead>Method</TableHead>
                      <TableHead>Результат</TableHead>
                      <TableHead>IP</TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {items.map((entry) => (
                      <TableRow key={entry.id}>
                        <TableCell className="text-sm">{dateFormatter.format(new Date(entry.createdAt))}</TableCell>
                        <TableCell className="text-sm">
                          {entry.adminUsername ?? entry.adminDisplayName ?? (
                            <code className="text-xs">{entry.adminId}</code>
                          )}
                        </TableCell>
                        <TableCell>
                          <Badge variant="outline" className="text-xs">
                            {entry.action}
                          </Badge>
                        </TableCell>
                        <TableCell>
                          {entry.targetUserId ? (
                            <code className="text-xs">{entry.targetUserId}</code>
                          ) : (
                            "—"
                          )}
                        </TableCell>
                        <TableCell className="font-mono text-xs">{entry.method}</TableCell>
                        <TableCell>
                          {entry.result === "success" ? (
                            <Badge variant="secondary">success</Badge>
                          ) : (
                            <Badge variant="destructive" title={entry.errorMessage ?? undefined}>
                              failure
                            </Badge>
                          )}
                        </TableCell>
                        <TableCell className="text-xs text-muted-foreground">
                          {entry.ipAddress ?? "—"}
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </div>
            </>
          )}
        </CardContent>
      </Card>

      {nextCursor ? (
        <div className="flex justify-end">
          <Button
            variant="outline"
            className="min-touch w-full sm:w-auto"
            onClick={() => setCursor(nextCursor)}
          >
            Следующая страница
            <ChevronRight className="ml-1 h-4 w-4" />
          </Button>
        </div>
      ) : null}
    </div>
  );
}
