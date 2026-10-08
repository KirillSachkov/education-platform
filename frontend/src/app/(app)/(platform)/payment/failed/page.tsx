"use client";

import { clearLastOrderId } from "@/features/buy-plan";
import { routes } from "@/shared/config/routes";
import { Button } from "@/shared/ui/kit/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/ui/kit/card";
import { Icons } from "@/shared/ui/icons";
import Link from "next/link";
import { useEffect } from "react";

/**
 * `/payment/failed` — landing после T-Bank fail (декланy, отмена,
 * timeout формы). На orderId pollить статус не имеет смысла — T-Bank уже
 * сообщил о failure. Чистим lastOrderId и показываем CTA «попробовать снова».
 */
export default function PaymentFailedPage() {
  useEffect(() => {
    clearLastOrderId();
  }, []);

  return (
    <div className="mx-auto mt-16 max-w-md px-4">
      <Card>
        <CardHeader className="items-center text-center">
          <span className="flex size-14 items-center justify-center rounded-full bg-destructive/10 text-destructive">
            <Icons.error className="size-8" />
          </span>
          <CardTitle className="mt-4 text-2xl">Платёж не прошёл</CardTitle>
        </CardHeader>
        <CardContent className="space-y-4 text-center">
          <p className="text-sm text-muted-foreground">
            Платёжная система отказала или вы вышли из формы оплаты. Если деньги
            всё-таки списались — они вернутся на карту в течение нескольких рабочих дней.
          </p>
          <div className="flex flex-col gap-2 sm:flex-row sm:justify-center">
            <Button asChild>
              <Link href={routes.pricing}>Выбрать план</Link>
            </Button>
            <Button asChild variant="outline">
              <Link href={routes.settings}>В настройки</Link>
            </Button>
          </div>
        </CardContent>
      </Card>
    </div>
  );
}
