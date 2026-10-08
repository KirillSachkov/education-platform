"use client";

import { profileQueryOptions } from "@/entities/profile";
import { useTelegramLink } from "@/features/telegram-link";
import { AUTH_ORIGIN } from "@/shared/config";
import { Icons } from "@/shared/ui/icons";
import { Button } from "@/shared/ui/kit/button";
import { Card } from "@/shared/ui/kit/card";
import { LogoMark } from "@/shared/ui/kit/logo";
import { useQuery } from "@tanstack/react-query";
import { useRouter } from "next/navigation";
import { resolveNextPath } from "./next-path";

type Props = {
  /** Сырое значение `?next=` — валидируется здесь через {@link resolveNextPath}. */
  next: string | null;
};

/**
 * Онбординг после OTP-регистрации: две карточки привязки (GitHub, Telegram)
 * + «Пропустить». Standalone-композиция на app-слое: plan-wizard step-views
 * завязаны на planId/invitation-флоу и здесь не подходят. GitHub — тот же
 * `${AUTH_ORIGIN}/auth/github/link`, что и в settings; Telegram — deep-link
 * + polling через `features/telegram-link.useTelegramLink` (public API).
 */
export function ConnectionsView({ next }: Props) {
  const router = useRouter();
  const nextPath = resolveNextPath(next);
  const { data: profile } = useQuery(profileQueryOptions.getMyProfileOptions());
  const { linkTelegram, isPending: isLinkingTelegram } = useTelegramLink();

  return (
    <div className="relative flex min-h-svh overflow-hidden bg-background">
      <div className="pointer-events-none absolute inset-0">
        <div className="absolute top-[10%] left-[15%] w-[400px] h-[400px] rounded-full bg-primary/4 blur-3xl" />
        <div className="absolute bottom-[10%] right-[10%] w-[350px] h-[350px] rounded-full bg-cyan/3 blur-3xl" />
      </div>

      <div className="relative m-auto flex w-full max-w-md flex-col items-center gap-8 px-4 py-10">
        <div className="flex flex-col items-center gap-4">
          <LogoMark size={48} className="text-primary" />
          <p className="text-sm font-bold font-[family-name:var(--font-sora)]">
            Sachkov<span className="text-primary">Learn</span>
          </p>
        </div>

        <div className="flex flex-col items-center gap-1 text-center">
          <h1 className="text-2xl font-semibold">Свяжите аккаунты</h1>
          <p className="text-sm text-muted-foreground">Это можно сделать позже в настройках</p>
        </div>

        <div className="flex w-full flex-col gap-4">
          <ConnectionCard
            icon={<Icons.github className="size-5" />}
            title="GitHub"
            description="Автоматический доступ к курсам по вашей организации и AI-проверка PR в заданиях."
            isLinked={profile?.hasGitHubLinked ?? false}
            linkedLabel="GitHub привязан"
            action={
              <Button asChild className="w-full">
                <a href={`${AUTH_ORIGIN}/auth/github/link`}>
                  <Icons.github className="size-4" />
                  Привязать GitHub
                </a>
              </Button>
            }
          />

          <ConnectionCard
            icon={<Icons.telegram className="size-5" />}
            title="Telegram"
            description="Чаты курсов, уведомления и помощь."
            isLinked={profile?.hasTelegramLinked ?? false}
            linkedLabel="Telegram привязан"
            action={
              <Button
                className="w-full"
                onClick={() => linkTelegram()}
                disabled={isLinkingTelegram}
              >
                <Icons.telegram className="size-4" />
                {isLinkingTelegram ? "Открываем бота…" : "Привязать Telegram"}
              </Button>
            }
          />
        </div>

        <Button
          variant="ghost"
          className="text-muted-foreground"
          onClick={() => router.push(nextPath)}
        >
          Пропустить
          <Icons.arrowRight className="size-4" />
        </Button>
      </div>
    </div>
  );
}

function ConnectionCard({
  icon,
  title,
  description,
  isLinked,
  linkedLabel,
  action,
}: {
  icon: React.ReactNode;
  title: string;
  description: string;
  isLinked: boolean;
  linkedLabel: string;
  action: React.ReactNode;
}) {
  return (
    <Card className="gap-3 p-5">
      <div className="flex items-center gap-3">
        <span className="flex size-9 shrink-0 items-center justify-center rounded-lg bg-muted/60 text-foreground">
          {icon}
        </span>
        <span className="font-medium">{title}</span>
      </div>
      <p className="text-sm text-muted-foreground">{description}</p>
      {isLinked ? (
        <div className="flex min-h-9 items-center gap-2 text-sm font-medium text-emerald-500">
          <Icons.check className="size-4" />
          {linkedLabel}
        </div>
      ) : (
        action
      )}
    </Card>
  );
}
