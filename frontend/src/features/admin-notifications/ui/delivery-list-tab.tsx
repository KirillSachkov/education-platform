"use client";

import { useInfiniteQuery } from "@tanstack/react-query";
import { useState } from "react";
import {
  adminNotificationQueryOptions,
  adminNotificationsApi,
  DeliveryChannelLabels,
  DeliveryStatusColors,
  DeliveryStatusLabels,
  type DeliveryListFilters,
  type DeliveryListItem,
} from "@/entities/admin-notifications";
import { formatShortDateWithTime } from "@/shared/lib/date";
import { Badge } from "@/shared/ui/kit/badge";
import { Button } from "@/shared/ui/kit/button";
import { Input } from "@/shared/ui/kit/input";
import { Label } from "@/shared/ui/kit/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/shared/ui/kit/select";
import { Icons } from "@/shared/ui/icons";

/**
 * Таб «Доставки»: фильтры (status / channel / recipient) + cursor-paginated таблица.
 * Клик на строку с error'ом открывает expandable detail с errorCode+errorDetail.
 */
export function DeliveryListTab() {
  const [filters, setFilters] = useState<DeliveryListFilters>({});

  const { data, fetchNextPage, hasNextPage, isFetchingNextPage, isLoading } = useInfiniteQuery({
    queryKey: [adminNotificationQueryOptions.baseKey, "deliveries-infinite", filters],
    queryFn: ({ pageParam, signal }) =>
      adminNotificationsApi.listDeliveries(
        {
          ...filters,
          cursorBefore: pageParam?.cursorBefore,
          cursorId: pageParam?.cursorId,
          limit: 50,
        },
        { signal },
      ),
    initialPageParam: undefined as { cursorBefore?: string; cursorId?: string } | undefined,
    getNextPageParam: (last) =>
      last.nextCursorBefore && last.nextCursorId
        ? { cursorBefore: last.nextCursorBefore, cursorId: last.nextCursorId }
        : undefined,
  });

  const items: DeliveryListItem[] = data?.pages.flatMap((p) => p.items) ?? [];

  return (
    <div className="space-y-4">
      {/* Filters */}
      <div className="grid grid-cols-1 md:grid-cols-4 gap-3 pb-4 border-b">
        <div>
          <Label className="text-xs">Статус</Label>
          <Select
            value={filters.status?.toString() ?? "all"}
            onValueChange={(v) =>
              setFilters((f) => ({ ...f, status: v === "all" ? undefined : Number(v) }))
            }
          >
            <SelectTrigger>
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="all">Все</SelectItem>
              <SelectItem value="0">Pending</SelectItem>
              <SelectItem value="1">Доставлено</SelectItem>
              <SelectItem value="2">Ошибка</SelectItem>
              <SelectItem value="3">Пропущено</SelectItem>
            </SelectContent>
          </Select>
        </div>

        <div>
          <Label className="text-xs">Канал</Label>
          <Select
            value={filters.channel?.toString() ?? "all"}
            onValueChange={(v) =>
              setFilters((f) => ({ ...f, channel: v === "all" ? undefined : Number(v) }))
            }
          >
            <SelectTrigger>
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="all">Все</SelectItem>
              <SelectItem value="1">InApp</SelectItem>
              <SelectItem value="2">Telegram</SelectItem>
              <SelectItem value="4">Email</SelectItem>
            </SelectContent>
          </Select>
        </div>

        <div className="md:col-span-2">
          <Label className="text-xs">Recipient userId (GUID)</Label>
          <Input
            placeholder="Фильтр по получателю"
            value={filters.recipientUserId ?? ""}
            onChange={(e) =>
              setFilters((f) => ({
                ...f,
                recipientUserId: e.target.value.trim() || undefined,
              }))
            }
          />
        </div>
      </div>

      {/* Table */}
      {isLoading ? (
        <div className="flex items-center justify-center py-10">
          <Icons.loading size={20} className="animate-spin text-muted-foreground" />
        </div>
      ) : items.length === 0 ? (
        <div className="text-center py-10 text-sm text-muted-foreground">
          Доставки по этим фильтрам не найдены
        </div>
      ) : (
        <div className="border rounded-lg overflow-x-auto">
          <table className="w-full text-sm">
            <thead className="bg-muted/40">
              <tr className="text-left">
                <th className="px-3 py-2 font-medium">Статус</th>
                <th className="px-3 py-2 font-medium">Канал</th>
                <th className="px-3 py-2 font-medium">Тип</th>
                <th className="px-3 py-2 font-medium hidden md:table-cell">Получатель</th>
                <th className="px-3 py-2 font-medium hidden md:table-cell">Создано</th>
                <th className="px-3 py-2 font-medium">Детали</th>
              </tr>
            </thead>
            <tbody>
              {items.map((d) => (
                <tr key={d.deliveryId} className="border-t hover:bg-accent/20">
                  <td className="px-3 py-2">
                    <Badge variant={DeliveryStatusColors[d.status] ?? "secondary"}>
                      {DeliveryStatusLabels[d.status] ?? d.status}
                    </Badge>
                  </td>
                  <td className="px-3 py-2">{DeliveryChannelLabels[d.channel] ?? d.channel}</td>
                  <td className="px-3 py-2 text-muted-foreground font-mono text-xs">
                    {d.templateId} ({d.type})
                  </td>
                  <td className="px-3 py-2 font-mono text-xs text-muted-foreground truncate max-w-[180px] hidden md:table-cell">
                    {d.recipientUserId}
                  </td>
                  <td className="px-3 py-2 text-xs text-muted-foreground hidden md:table-cell">
                    {formatShortDateWithTime(d.createdAt)}
                  </td>
                  <td className="px-3 py-2 text-xs">
                    {d.errorCode ? (
                      <span className="text-destructive" title={d.errorDetail ?? undefined}>
                        {d.errorCode}
                      </span>
                    ) : d.providerMessageId ? (
                      <span className="font-mono text-[11px] text-muted-foreground truncate inline-block max-w-[200px]">
                        {d.providerMessageId}
                      </span>
                    ) : (
                      <span className="text-muted-foreground">—</span>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {hasNextPage && (
        <div className="flex justify-center pt-2">
          <Button variant="outline" onClick={() => fetchNextPage()} disabled={isFetchingNextPage}>
            {isFetchingNextPage ? (
              <>
                <Icons.loading size={14} className="animate-spin mr-2" />
                Загружаем...
              </>
            ) : (
              "Показать ещё"
            )}
          </Button>
        </div>
      )}
    </div>
  );
}
