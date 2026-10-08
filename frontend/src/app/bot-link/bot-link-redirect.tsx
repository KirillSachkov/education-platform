"use client";

import { planTelegramChatQueryOptions } from "@/entities/plan-telegram-chat";
import { usersAdminApi } from "@/entities/user";
import { useMyProfile } from "@/features/profile-manage";
import { getErrorMessage } from "@/shared/api";
import { Icons } from "@/shared/ui/icons";
import { useQuery } from "@tanstack/react-query";
import { useEffect } from "react";

const REDIRECT_DELAY_MS = 1500;

export function BotLinkRedirect() {
  const { profile, isPending: isProfilePending, error: profileError } = useMyProfile();

  // Генерация link-token'а — только когда у нас есть профиль и TG ещё не привязан.
  const isReadyToGenerate = !!profile && !profile.hasTelegramLinked;

  const {
    data: tokenData,
    error: tokenError,
    isFetching: isTokenFetching,
  } = useQuery({
    queryKey: [planTelegramChatQueryOptions.baseKey, "link-token"] as const,
    queryFn: () => usersAdminApi.getTelegramLinkToken(),
    enabled: isReadyToGenerate,
    staleTime: 0,
    gcTime: 0,
  });

  // Auto-redirect через 1.5 сек как только получили deepLinkUrl.
  useEffect(() => {
    if (!tokenData?.deepLinkUrl) return;
    const timeoutId = window.setTimeout(() => {
      window.location.href = tokenData.deepLinkUrl;
    }, REDIRECT_DELAY_MS);
    return () => window.clearTimeout(timeoutId);
  }, [tokenData]);

  // Derive UI state из query/profile состояний — никаких setState в effect.
  const status: "loading" | "redirecting" | "already_linked" | "error" = profileError
    ? "error"
    : isProfilePending
      ? "loading"
      : !profile
        ? "error"
        : profile.hasTelegramLinked
          ? "already_linked"
          : tokenError
            ? "error"
            : tokenData?.deepLinkUrl
              ? "redirecting"
              : isTokenFetching
                ? "loading"
                : "loading";

  const errorMessage = profileError
    ? getErrorMessage(profileError, "Не удалось загрузить профиль")
    : tokenError
      ? getErrorMessage(tokenError, "Не удалось сгенерировать ссылку привязки")
      : null;

  return (
    <div className="flex min-h-[60vh] items-center justify-center px-4">
      <div className="max-w-md text-center space-y-4">
        {status === "loading" && (
          <>
            <Icons.loading className="mx-auto size-10 animate-spin text-muted-foreground" />
            <p className="text-sm text-muted-foreground">Подготавливаем привязку…</p>
          </>
        )}

        {status === "redirecting" && tokenData && (
          <>
            <Icons.telegram className="mx-auto size-12 text-blue" />
            <h1 className="text-lg font-semibold">Открываем Telegram…</h1>
            <p className="text-sm text-muted-foreground">
              Если ничего не открывается, нажми вручную:
            </p>
            <a
              href={tokenData.deepLinkUrl}
              className="inline-flex items-center gap-2 rounded-md bg-primary px-4 py-2 text-sm font-medium text-primary-foreground hover:opacity-90"
            >
              <Icons.externalLink className="size-4" />
              Перейти в бот
            </a>
          </>
        )}

        {status === "already_linked" && (
          <>
            <Icons.success className="mx-auto size-12 text-teal" />
            <h1 className="text-lg font-semibold">Telegram уже привязан</h1>
            <p className="text-sm text-muted-foreground">
              Открой бот — все возможности уже доступны.
            </p>
          </>
        )}

        {status === "error" && (
          <>
            <Icons.error className="mx-auto size-12 text-destructive" />
            <h1 className="text-lg font-semibold">Что-то пошло не так</h1>
            <p className="text-sm text-muted-foreground">{errorMessage}</p>
          </>
        )}
      </div>
    </div>
  );
}
