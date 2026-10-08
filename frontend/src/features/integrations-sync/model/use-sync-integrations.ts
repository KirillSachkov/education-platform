"use client";

import { profileQueryOptions } from "@/entities/profile";
import { usersAdminApi } from "@/entities/user";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

const POLL_ATTEMPTS = 5;
const POLL_INTERVAL_MS = 600;

/**
 * Единая «Синхронизировать всё» кнопка. Параллельно дёргает:
 *  1. POST /users/me/integrations/sync — пере-публикует UserGithubLogin для cached orgs
 *     (ProgressService подтянет недостающие enrollment'ы).
 *  2. POST /telegram/me/resync-invites — TelegramBotService пере-разошлёт DM-инвайты.
 *
 * Phase E (#45): legacy course-tag resync удалён — plan-tags теперь ответственность
 * AccessService self-consume sync handler (canonical path). Если юзер видит замки
 * на курсах, доступ к которым должен быть — это инцидент в AccessService, не drift
 * в ProgressService.
 *
 * Использует кэш — не дёргает GitHub API. Если юзер только что вступил в новую org —
 * нужен отдельный «Hard refresh» через OAuth-редирект на /auth/github/sync-courses.
 */
export function useSyncIntegrations() {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: async () => {
      const [github, telegram] = await Promise.allSettled([
        usersAdminApi.syncIntegrations(),
        usersAdminApi.resyncTelegramInvites(),
      ]);

      return {
        matchedGithubOrgs: github.status === "fulfilled" ? github.value.matchedGithubOrgs : [],
        githubSyncTriggered:
          github.status === "fulfilled" ? github.value.githubSyncTriggered : false,
        invitesSent: telegram.status === "fulfilled" ? telegram.value.invitesSent : 0,
        telegramLinked: telegram.status === "fulfilled" ? telegram.value.telegramLinked : false,
        githubError: github.status === "rejected" ? github.reason : null,
        telegramError: telegram.status === "rejected" ? telegram.reason : null,
      };
    },
    onSuccess: (data) => {
      // Если хоть один из ожидаемых сервисов упал — показать ошибку. «Ожидаемых» = который
      // у юзера привязан (в backend профиле). Без проверки можно было бы показывать
      // misleading info-toast «привяжите GitHub или Telegram», когда у юзера, скажем,
      // Telegram привязан, а resync XHR упал по сети.
      const profile = queryClient.getQueryData<
        { hasGitHubLinked: boolean; hasTelegramLinked: boolean } | undefined
      >(profileQueryOptions.getMyProfileKey());

      const githubExpected = profile?.hasGitHubLinked ?? false;
      const telegramExpected = profile?.hasTelegramLinked ?? false;

      const githubFailed = githubExpected && data.githubError !== null;
      const telegramFailed = telegramExpected && data.telegramError !== null;

      if (githubFailed && telegramFailed) {
        toast.error("Ошибка синхронизации");
        return;
      }

      const parts: string[] = [];
      if (data.githubSyncTriggered) {
        parts.push(
          data.matchedGithubOrgs.length > 0
            ? `совпадение по ${data.matchedGithubOrgs.length} GitHub-org`
            : "GitHub: совпадений нет",
        );
      }
      if (data.telegramLinked) {
        parts.push(
          data.invitesSent > 0
            ? `${data.invitesSent} приглашений в чаты`
            : "Telegram: новых чатов нет",
        );
      }
      if (githubFailed) parts.push("GitHub: ошибка");
      if (telegramFailed) parts.push("Telegram: ошибка");

      if (parts.length === 0) {
        // Ни один сервис не привязан — info-tone, не warning.
        toast.info("Привяжите GitHub или Telegram, чтобы было что синхронизировать");
        return;
      }

      if (githubFailed || telegramFailed) {
        toast.warning(`Синхронизация частично: ${parts.join(", ")}`);
      } else {
        toast.success(`Синхронизировано: ${parts.join(", ")}`);
      }
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка синхронизации"));
    },
    onSettled: async () => {
      // Profile обновляется сразу после ответа sync-эндпоинта (GithubOrgs могли поменяться).
      await queryClient.invalidateQueries({
        queryKey: profileQueryOptions.getMyProfileKey(),
      });

      // Polling enrollments + chats. Backend синхронно пишет UserGithubLogin в outbox,
      // ProgressService обработает event асинхронно через Wolverine. Обычно это 100-500 ms,
      // под нагрузкой на очередь — дольше. Делаем N попыток invalidateQueries с шагом
      // POLL_INTERVAL_MS — TanStack Query сам сделает refetch для активных observer'ов.
      // Если данные уже на свежей версии — повторный refetch дёшев (HTTP-кэш + stale-while-revalidate).
      //
      // Альтернативы (отложены): SSE-стрим из ProgressService с push-нотификацией
      // о завершении enrollment'а / Wolverine saga с blocking-ожиданием handler'а в endpoint'е.
      // Любая требует отдельного MR.
      //
      // Если юзер ушёл со страницы — invalidateQueries безопасно: TanStack Query не
      // триггерит refetch для queries без активных observer'ов, только помечает stale.
      for (let i = 0; i < POLL_ATTEMPTS; i++) {
        await new Promise((resolve) => setTimeout(resolve, POLL_INTERVAL_MS));
        await Promise.all([
          queryClient.invalidateQueries({ queryKey: ["enrollments"] }),
          queryClient.invalidateQueries({ queryKey: ["telegram", "my-chats"] }),
        ]);
      }
    },
  });

  return {
    syncIntegrations: mutation.mutate,
    isPending: mutation.isPending,
  };
}
