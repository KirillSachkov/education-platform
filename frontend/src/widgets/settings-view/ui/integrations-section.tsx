"use client";

import { useEffect, useState } from "react";
import { useSearchParams } from "next/navigation";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import type { MyProfile } from "@/entities/profile";
import { planTelegramChatQueryOptions } from "@/entities/plan-telegram-chat";
import { useMyProfile } from "@/features/profile-manage";
import { useTelegramLink, useTelegramUnlink } from "@/features/telegram-link";
import { useUnlinkGitHub } from "@/features/github-link";
import { useSyncIntegrations } from "@/features/integrations-sync";
import { ReviewAppCard } from "@/features/connect-review-app";
import { ROLES } from "@/shared/auth/roles";
import { AUTH_ORIGIN } from "@/shared/config";
import { Icons } from "@/shared/ui/icons";
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
  AlertDialogTrigger,
} from "@/shared/ui/kit/alert-dialog";
import { Button } from "@/shared/ui/kit/button";
import { Skeleton } from "@/shared/ui/kit/skeleton";
import { AccountStatusBanner } from "./account-status-banner";

/**
 * Раздел «Интеграции» — единое место для всех связанных сервисов.
 * GitHub, Telegram (привязка профиля + список доступных чатов курсов).
 */
export function IntegrationsSection() {
  const searchParams = useSearchParams();
  // GitHub-sync OAuth-flow редиректит сюда с ?sync=...; GitHub-link callback
  // (в т.ч. ошибки типа уже-привязанный-аккаунт) — с ?account=...
  // ARS GitHub App install callback — с ?installation=success|error&code=...
  const syncParam = searchParams.get("sync");
  const accountParam = searchParams.get("account");
  const installationParam = searchParams.get("installation");
  const initial = syncParam ? `sync-${syncParam}` : accountParam ? `account-${accountParam}` : null;
  const [bannerStatus, setBannerStatus] = useState<string | null>(initial);
  const queryClient = useQueryClient();

  // ARS install callback: toast + invalidate installations query.
  useEffect(() => {
    if (!installationParam) return;
    if (installationParam === "success") {
      toast.success("GitHub App установлен");
      queryClient.invalidateQueries({
        queryKey: ["assignment-review", "installations", "me"],
      });
    } else if (installationParam === "error") {
      const code = searchParams.get("code");
      toast.error(code ? `Не удалось установить приложение: ${code}` : "Не удалось установить приложение");
    }
  }, [installationParam, queryClient, searchParams]);

  const { profile, isPending } = useMyProfile();

  if (isPending) {
    return (
      <div className="space-y-3">
        <Skeleton className="h-20 w-full rounded-2xl" />
        <Skeleton className="h-20 w-full rounded-2xl" />
      </div>
    );
  }

  if (!profile) {
    return <p className="text-sm text-destructive">Не удалось загрузить профиль.</p>;
  }

  return (
    <div className="space-y-6">
      <AccountStatusBanner status={bannerStatus} onDismiss={() => setBannerStatus(null)} />

      <SectionGroup title="Связанные аккаунты">
        <GitHubRow profile={profile} />
        <Divider />
        <TelegramRow profile={profile} />
      </SectionGroup>

      {(profile.hasGitHubLinked || profile.hasTelegramLinked) && <SyncHubBlock profile={profile} />}

      {profile.hasGitHubLinked && profile.githubOrgs.length > 0 && (
        <GithubOrgsBlock orgs={profile.githubOrgs} />
      )}

      <SectionGroup title="AI-проверка PR'ов">
        <ReviewAppCard
          isAuthor={
            profile.roles.includes(ROLES.AUTHOR) ||
            profile.roles.includes(ROLES.EDITOR) ||
            profile.roles.includes(ROLES.ADMIN) ||
            profile.roles.includes(ROLES.OWNER)
          }
        />
      </SectionGroup>

      {profile.hasTelegramLinked && <PlanChatsSection profile={profile} />}
    </div>
  );
}

/* ───────────────── Sync Hub ───────────────── */

function SyncHubBlock({ profile }: { profile: MyProfile }) {
  const { syncIntegrations, isPending } = useSyncIntegrations();

  return (
    <SectionGroup
      title="Синхронизация"
      description="Перепроверим: какие курсы открыты по GitHub-org, в какие Telegram-чаты можно войти. Запускается без побочных эффектов."
    >
      <div className="rounded-xl px-3 py-3 sm:px-4 sm:py-4">
        <div className="flex flex-col items-start gap-3 sm:flex-row sm:items-center sm:justify-between">
          <div className="space-y-1">
            <p className="text-sm font-medium text-foreground">Синхронизировать всё</p>
            <p className="text-xs text-muted-foreground/90">
              Проверим GitHub-organizations и Telegram-чаты против ваших записей.
            </p>
          </div>

          <Button
            size="default"
            onClick={() => syncIntegrations()}
            disabled={isPending}
            className="w-full sm:w-auto"
          >
            {isPending ? (
              <Icons.loading className="size-4 animate-spin" />
            ) : (
              <Icons.refresh className="size-4" />
            )}
            {isPending ? "Синхронизируем…" : "Синхронизировать всё"}
          </Button>
        </div>

        {profile.hasGitHubLinked && (
          <p className="mt-3 text-xs text-muted-foreground/70">
            Только что вступили в новую GitHub-organization?{" "}
            <a
              href={`${AUTH_ORIGIN}/auth/github/sync-courses`}
              className="underline underline-offset-2 hover:text-foreground"
            >
              Обновить список из GitHub
            </a>
            {" — это перейдёт через OAuth и подтянет свежие orgs."}
          </p>
        )}
      </div>
    </SectionGroup>
  );
}

/* ───────────────── GitHub Orgs ───────────────── */

function GithubOrgsBlock({ orgs }: { orgs: string[] }) {
  return (
    <SectionGroup
      title="GitHub-организации"
      description="Кэш ваших GitHub-orgs. Используется для авто-зачисления на привязанные курсы."
    >
      <ul className="flex flex-col">
        {orgs.map((org, idx) => (
          <li key={`${org}-${idx}`}>
            <IntegrationRow
              icon={<Icons.github className="size-4" />}
              iconClass="bg-muted/60 text-foreground"
              title={org}
              description="GitHub organization"
              action={
                <Button asChild size="sm" variant="ghost">
                  <a href={`https://github.com/${org}`} target="_blank" rel="noopener noreferrer">
                    <Icons.externalLink className="size-3.5" />
                    GitHub
                  </a>
                </Button>
              }
            />
            {idx < orgs.length - 1 && <Divider />}
          </li>
        ))}
      </ul>
    </SectionGroup>
  );
}

/* ───────────────── Rows ───────────────── */

function GitHubRow({ profile }: { profile: MyProfile }) {
  const { unlinkGitHub, isPending } = useUnlinkGitHub();
  const isLinked = profile.hasGitHubLinked;

  return (
    <IntegrationRow
      icon={<Icons.github className="size-4" />}
      iconClass="bg-muted/60 text-foreground"
      title={isLinked ? "GitHub привязан" : "GitHub"}
      badge={
        !isLinked && (
          <span className="inline-flex shrink-0 items-center rounded-full bg-primary/10 px-2 py-0.5 text-[11px] font-medium text-primary">
            рекомендуем привязать
          </span>
        )
      }
      description="Автоматический доступ к курсам по GitHub-организации и AI-ревью PR."
      action={
        isLinked ? (
          <Button
            variant="ghost"
            size="sm"
            className="text-muted-foreground hover:text-destructive"
            onClick={() => unlinkGitHub()}
            disabled={isPending}
          >
            {isPending ? (
              <Icons.loading className="size-3.5 animate-spin" />
            ) : (
              <Icons.unlink className="size-3.5" />
            )}
            Отвязать
          </Button>
        ) : (
          <Button asChild size="sm">
            <a href={`${AUTH_ORIGIN}/auth/github/link`}>
              <Icons.attachment className="size-3.5" />
              Привязать
            </a>
          </Button>
        )
      }
    />
  );
}

function TelegramRow({ profile }: { profile: MyProfile }) {
  const { linkTelegram, isPending: isLinking } = useTelegramLink();
  const { unlinkTelegram, isPending: isUnlinking } = useTelegramUnlink();
  const isLinked = profile.hasTelegramLinked;

  return (
    <IntegrationRow
      icon={<Icons.telegram className="size-4" />}
      iconClass="bg-blue-dim text-blue"
      title={isLinked ? "Telegram привязан" : "Telegram"}
      description={
        isLinked
          ? "Уведомления приходят в бот, открывается доступ в чаты курсов"
          : "Привяжите, чтобы получать уведомления в бот и попадать в чаты курсов"
      }
      action={
        isLinked ? (
          <AlertDialog>
            <AlertDialogTrigger asChild>
              <Button
                variant="ghost"
                size="sm"
                className="text-muted-foreground hover:text-destructive"
                disabled={isUnlinking}
              >
                {isUnlinking ? (
                  <Icons.loading className="size-3.5 animate-spin" />
                ) : (
                  <Icons.unlink className="size-3.5" />
                )}
                Отвязать
              </Button>
            </AlertDialogTrigger>
            <AlertDialogContent>
              <AlertDialogHeader>
                <AlertDialogTitle>Отвязать Telegram?</AlertDialogTitle>
                <AlertDialogDescription>
                  Вы перестанете получать уведомления в Telegram-боте и потеряете доступ в чаты
                  курсов. Привязку можно восстановить в любой момент.
                </AlertDialogDescription>
              </AlertDialogHeader>
              <AlertDialogFooter>
                <AlertDialogCancel>Отмена</AlertDialogCancel>
                <AlertDialogAction onClick={() => unlinkTelegram()}>Отвязать</AlertDialogAction>
              </AlertDialogFooter>
            </AlertDialogContent>
          </AlertDialog>
        ) : (
          <Button size="sm" onClick={() => linkTelegram()} disabled={isLinking}>
            {isLinking ? (
              <Icons.loading className="size-3.5 animate-spin" />
            ) : (
              <Icons.attachment className="size-3.5" />
            )}
            Привязать
          </Button>
        )
      }
    />
  );
}

/* ───────────────── Plan chats ───────────────── */

function PlanChatsSection({ profile }: { profile: MyProfile }) {
  const {
    data: chats,
    isLoading,
    error,
  } = useQuery({
    ...planTelegramChatQueryOptions.myChats(),
    enabled: profile.hasTelegramLinked,
  });

  const chatList = chats ?? [];

  return (
    <SectionGroup
      title="Чаты планов"
      description="Telegram-чаты по вашим активным планам. Бот пустит автоматически."
    >
      {isLoading ? (
        <div className="space-y-2 px-3 py-3">
          <Skeleton className="h-14 w-full" />
          <Skeleton className="h-14 w-full" />
        </div>
      ) : error ? (
        <p className="px-3 py-3 text-sm text-destructive">Не удалось загрузить список чатов</p>
      ) : chatList.length === 0 ? (
        <p className="px-3 py-3 text-sm text-muted-foreground">
          По вашим активным планам пока не привязано ни одного Telegram-чата.
        </p>
      ) : (
        <ul className="flex flex-col">
          {chatList.map((chat, idx) => {
            const titles = chat.planTitles.filter((t) => t.length > 0);
            const plansLabel = titles.length > 0 ? titles.join(" · ") : null;
            const subtitle = [chat.chatType === "CHANNEL" ? "Канал" : "Группа", plansLabel]
              .filter(Boolean)
              .join(" · ");
            return (
              <li key={chat.telegramChatId}>
                <IntegrationRow
                  icon={<Icons.message className="size-4" />}
                  iconClass="bg-blue-dim text-blue"
                  title={chat.chatTitle ?? "Без названия"}
                  description={subtitle || "—"}
                  action={
                    <Button asChild size="sm" variant={chat.isMember ? "ghost" : "outline"}>
                      <a href={chat.inviteLink} target="_blank" rel="noopener noreferrer">
                        <Icons.externalLink className="size-3.5" />
                        {chat.isMember ? "Открыть" : "Войти"}
                      </a>
                    </Button>
                  }
                />
                {idx < chatList.length - 1 && <Divider />}
              </li>
            );
          })}
        </ul>
      )}
    </SectionGroup>
  );
}

/* ───────────────── Primitives ───────────────── */

function SectionGroup({
  title,
  description,
  children,
}: {
  title: string;
  description?: string;
  children: React.ReactNode;
}) {
  return (
    <section className="space-y-3">
      <header className="px-1 space-y-0.5">
        <h3 className="text-xs font-semibold text-muted-foreground uppercase tracking-wider">
          {title}
        </h3>
        {description && <p className="text-xs text-muted-foreground/80">{description}</p>}
      </header>
      <div className="rounded-2xl border border-border/50 bg-card/40 p-2 sm:p-3">{children}</div>
    </section>
  );
}

function IntegrationRow({
  icon,
  iconClass,
  title,
  badge,
  description,
  action,
}: {
  icon: React.ReactNode;
  iconClass?: string;
  title: string;
  badge?: React.ReactNode;
  description: string;
  action: React.ReactNode;
}) {
  return (
    <div className="flex items-center gap-3.5 rounded-xl px-3 py-3 hover:bg-muted/40 transition-colors">
      <span
        className={`flex size-9 shrink-0 items-center justify-center rounded-xl ${
          iconClass ?? "bg-muted/60 text-muted-foreground"
        }`}
      >
        {icon}
      </span>
      <div className="min-w-0 flex-1">
        <div className="flex min-w-0 items-center gap-2">
          <p className="text-sm font-medium text-foreground truncate">{title}</p>
          {badge}
        </div>
        <p className="mt-0.5 text-xs text-muted-foreground/90 line-clamp-2">{description}</p>
      </div>
      <div className="shrink-0">{action}</div>
    </div>
  );
}

function Divider() {
  return <div className="mx-3 h-px bg-border/40" />;
}
