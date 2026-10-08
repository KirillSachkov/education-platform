"use client";

import { invitePreviewQueryOptions } from "@/entities/access-plan";
import { useIsAuthenticated } from "@/shared/auth/use-is-authenticated";
import { routes } from "@/shared/config/routes";
import { Button } from "@/shared/ui/kit/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/shared/ui/kit/card";
import { useQuery } from "@tanstack/react-query";
import { CheckCircle2 } from "lucide-react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useRedeemInvite } from "../model/use-redeem-invite";

interface InviteLandingViewProps {
  token: string;
}

const UNAVAILABLE_COPY: Record<string, { title: string; body: string }> = {
  "invite.revoked": {
    title: "Эта ссылка отозвана",
    body: "Автор отозвал приглашение. Попроси новую ссылку у того, кто её прислал.",
  },
  "invite.expired": {
    title: "Срок действия ссылки истёк",
    body: "Время для активации этого приглашения вышло. Попроси новую ссылку.",
  },
  "invite.usage.exhausted": {
    title: "Приглашение уже использовано",
    body: "Эта ссылка одноразовая или достигла лимита активаций.",
  },
};

export function InviteLandingView({ token }: InviteLandingViewProps) {
  const router = useRouter();
  const isAuthenticated = useIsAuthenticated();
  const previewQuery = useQuery(invitePreviewQueryOptions(token));
  const redeem = useRedeemInvite();

  if (previewQuery.isLoading) {
    return <InviteSkeleton />;
  }

  if (previewQuery.isError || !previewQuery.data) {
    return (
      <Card className="mx-auto mt-16 max-w-lg">
        <CardHeader>
          <CardTitle>Приглашение не найдено</CardTitle>
          <CardDescription>
            Проверь ссылку — возможно, она набрана не полностью или ведёт в никуда.
          </CardDescription>
        </CardHeader>
      </Card>
    );
  }

  const preview = previewQuery.data;

  if (!preview.isAvailable) {
    const copy = preview.unavailableReason
      ? UNAVAILABLE_COPY[preview.unavailableReason]
      : undefined;
    return (
      <Card className="mx-auto mt-16 max-w-lg">
        <CardHeader>
          <CardTitle>{copy?.title ?? "Приглашение недоступно"}</CardTitle>
          <CardDescription>{copy?.body ?? "Обратись к автору за новой ссылкой."}</CardDescription>
        </CardHeader>
      </Card>
    );
  }

  const onActivate = async () => {
    if (!isAuthenticated) {
      const callback = encodeURIComponent(routes.invite(token));
      router.push(`${routes.login}?callbackUrl=${callback}`);
      return;
    }

    const result = await redeem.mutateAsync(token);
    if (result.result) {
      // После активации ведём в каталог курсов — пользователь видит,
      // что именно открылось (а не возвращается на /home, где это неочевидно).
      router.push(routes.courses);
    }
  };

  return (
    <div className="mx-auto mt-12 max-w-2xl space-y-6 px-4">
      <header className="text-center space-y-3">
        <p className="text-sm uppercase tracking-wide text-muted-foreground">Тебе открыт доступ</p>
        <h1 className="text-2xl font-semibold sm:text-3xl">{preview.planDisplayName}</h1>
        {preview.planShortDescription && (
          <p className="text-muted-foreground">{preview.planShortDescription}</p>
        )}
      </header>

      {preview.planFeatures.length > 0 && (
        <Card>
          <CardHeader>
            <CardTitle className="text-lg">Что входит</CardTitle>
          </CardHeader>
          <CardContent>
            <ul className="space-y-2">
              {preview.planFeatures.map((feature) => (
                <li key={feature} className="flex items-start gap-2 text-sm">
                  <CheckCircle2 className="mt-0.5 size-4 shrink-0 text-emerald-500" />
                  <span>{feature}</span>
                </li>
              ))}
            </ul>
          </CardContent>
        </Card>
      )}

      {preview.includesFutureContent && (
        <p className="text-center text-sm text-muted-foreground">
          Включая новые материалы и курсы программы, которые выйдут позже.
        </p>
      )}

      <div className="flex flex-col items-center gap-3">
        <Button size="lg" onClick={onActivate} disabled={redeem.isPending} className="min-w-56">
          {redeem.isPending
            ? "Активируем..."
            : isAuthenticated
              ? "Активировать"
              : "Войти и активировать"}
        </Button>
        {!isAuthenticated && (
          <p className="text-xs text-muted-foreground">
            После входа ты вернёшься сюда автоматически.
          </p>
        )}
        <Link href={routes.landing} className="text-sm text-muted-foreground hover:underline">
          На главную
        </Link>
      </div>
    </div>
  );
}

function InviteSkeleton() {
  return (
    <div className="mx-auto mt-12 max-w-2xl space-y-6 px-4">
      <div className="space-y-3 text-center">
        <div className="mx-auto h-3 w-32 rounded bg-muted animate-pulse" />
        <div className="mx-auto h-8 w-2/3 rounded bg-muted animate-pulse" />
      </div>
      <div className="h-40 rounded bg-muted animate-pulse" />
    </div>
  );
}
