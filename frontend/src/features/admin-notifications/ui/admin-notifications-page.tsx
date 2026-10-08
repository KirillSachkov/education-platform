"use client";

import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/shared/ui/kit/card";
import { Separator } from "@/shared/ui/kit/separator";
import {
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
} from "@/shared/ui/kit/tabs";
import { Icons } from "@/shared/ui/icons";
import { DeliveryListTab } from "./delivery-list-tab";
import { DeliveryStatsTab } from "./delivery-stats-tab";

/**
 * Admin-only page /admin/notifications. Два таба:
 *   1) Доставки — list с фильтрами (status, channel, recipient, dates), keyset pagination.
 *   2) Статистика — агрегаты по каналам, типам, топ-10 причин failure.
 *
 * Permission gate стоит на layout (RequireRole atLeast=admin) + backend endpoints
 * (RequirePermissions Platform.ADMIN) — двойная защита.
 */
export function AdminNotificationsPage() {
  return (
    <div className="space-y-6 max-w-[1400px] mx-auto">
      <Card className="relative overflow-hidden">
        <div className="absolute top-0 inset-x-0 h-0.5 bg-gradient-primary" />
        <CardHeader>
          <CardTitle className="flex items-center gap-2">
            <Icons.notification size={20} />
            Журнал доставок уведомлений
          </CardTitle>
          <CardDescription className="pt-1">
            Для операционного триажа: статус каждой попытки доставки по каналам
            InApp / Telegram / Email + агрегированная статистика за период.
          </CardDescription>
        </CardHeader>
      </Card>

      <Card>
        <Separator />
        <CardContent className="pt-6">
          <Tabs defaultValue="deliveries">
            <TabsList className="w-full justify-start mb-6">
              <TabsTrigger value="deliveries" className="gap-1.5">
                <Icons.list size={16} />
                Доставки
              </TabsTrigger>
              <TabsTrigger value="stats" className="gap-1.5">
                <Icons.chart size={16} />
                Статистика
              </TabsTrigger>
            </TabsList>

            <TabsContent value="deliveries">
              <DeliveryListTab />
            </TabsContent>

            <TabsContent value="stats">
              <DeliveryStatsTab />
            </TabsContent>
          </Tabs>
        </CardContent>
      </Card>
    </div>
  );
}
