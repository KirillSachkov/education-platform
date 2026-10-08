"use client";

import { useQuery } from "@tanstack/react-query";
import {
  adminNotificationQueryOptions,
  DeliveryChannelLabels,
  DeliveryStatusColors,
  DeliveryStatusLabels,
} from "@/entities/admin-notifications";
import { Badge } from "@/shared/ui/kit/badge";
import { Icons } from "@/shared/ui/icons";

const NOTIFICATION_TYPE_LABELS: Record<number, string> = {
  1: "Welcome",
  2: "CourseEnrolled",
  3: "MaterialPublished",
  4: "IssueCreated",
  5: "IssueApproved",
  6: "IssueChangesRequested",
  7: "AuthorAnnouncement",
  8: "TelegramLinked",
  9: "IssueAwaitingReview",
  10: "CommentReplied",
  11: "CommentOnOwnContent",
};

/**
 * Таб «Статистика» — 3 виджета:
 *   • По каналам × статусам (matrix)
 *   • По типам (top list)
 *   • Топ-10 причин failure
 * Диапазон: последние 7 дней (default backend'а).
 */
export function DeliveryStatsTab() {
  const { data, isLoading, error } = useQuery(adminNotificationQueryOptions.stats());

  if (isLoading) {
    return (
      <div className="flex items-center justify-center py-10">
        <Icons.loading size={20} className="animate-spin text-muted-foreground" />
      </div>
    );
  }

  if (error || !data) {
    return (
      <div className="flex items-center gap-2 text-sm text-destructive">
        <Icons.error size={16} />
        Не удалось загрузить статистику
      </div>
    );
  }

  // Группируем per-channel × status в матрицу.
  const channels = [1, 2, 4];
  const statuses = [0, 1, 2, 3];
  const matrix: Record<string, number> = {};
  for (const b of data.perChannel) matrix[`${b.channel}-${b.status}`] = b.count;

  return (
    <div className="space-y-6">
      <p className="text-sm text-muted-foreground">
        Сводка за последние 7 дней. Пустые ячейки = 0.
      </p>

      {/* Channel × status matrix */}
      <section>
        <h3 className="text-sm font-semibold mb-2">По каналам и статусам</h3>
        <div className="border rounded-lg overflow-x-auto">
          <table className="w-full min-w-[28rem] text-sm">
            <thead className="bg-muted/40 text-left">
              <tr>
                <th className="px-3 py-2 font-medium">Канал</th>
                {statuses.map((s) => (
                  <th key={s} className="px-3 py-2 font-medium text-right">
                    <Badge variant={DeliveryStatusColors[s] ?? "secondary"}>
                      {DeliveryStatusLabels[s]}
                    </Badge>
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {channels.map((c) => (
                <tr key={c} className="border-t">
                  <td className="px-3 py-2 font-medium">{DeliveryChannelLabels[c]}</td>
                  {statuses.map((s) => (
                    <td key={s} className="px-3 py-2 text-right tabular-nums">
                      {matrix[`${c}-${s}`] ?? 0}
                    </td>
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>

      {/* Top types */}
      <section>
        <h3 className="text-sm font-semibold mb-2">По типам уведомлений</h3>
        <div className="border rounded-lg overflow-hidden">
          <table className="w-full text-sm">
            <thead className="bg-muted/40 text-left">
              <tr>
                <th className="px-3 py-2 font-medium">Тип</th>
                <th className="px-3 py-2 font-medium text-right">Кол-во</th>
              </tr>
            </thead>
            <tbody>
              {data.perType.length === 0 ? (
                <tr className="border-t">
                  <td className="px-3 py-2 text-muted-foreground" colSpan={2}>
                    Нет данных
                  </td>
                </tr>
              ) : (
                data.perType.map((t) => (
                  <tr key={t.type} className="border-t">
                    <td className="px-3 py-2">
                      {NOTIFICATION_TYPE_LABELS[t.type] ?? `Тип ${t.type}`}
                    </td>
                    <td className="px-3 py-2 text-right tabular-nums">{t.count}</td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      </section>

      {/* Top failures */}
      <section>
        <h3 className="text-sm font-semibold mb-2">Топ причин failure (Status=Failed)</h3>
        {data.topFailures.length === 0 ? (
          <p className="text-sm text-muted-foreground px-3 py-4 border rounded-lg">Ошибок нет.</p>
        ) : (
          <div className="border rounded-lg overflow-hidden">
            <table className="w-full text-sm">
              <thead className="bg-muted/40 text-left">
                <tr>
                  <th className="px-3 py-2 font-medium">Код ошибки</th>
                  <th className="px-3 py-2 font-medium text-right">Кол-во</th>
                </tr>
              </thead>
              <tbody>
                {data.topFailures.map((f) => (
                  <tr key={f.errorCode} className="border-t">
                    <td className="px-3 py-2 font-mono text-xs text-destructive">{f.errorCode}</td>
                    <td className="px-3 py-2 text-right tabular-nums">{f.count}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </div>
  );
}
