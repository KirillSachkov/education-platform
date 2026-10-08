"use client";

import { notificationQueryOptions } from "@/entities/notification";
import { profileQueryOptions, type MyProfile } from "@/entities/profile";
import { usersAdminApi } from "@/entities/user";
import { getErrorMessage, type Envelope } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useEffect, useRef } from "react";
import { toast } from "sonner";

const POLL_INTERVAL_MS = 3000;
const POLL_TIMEOUT_MS = 120_000; // 2 минуты — юзер успеет зайти в TG, нажать /start, вернуться

/**
 * Открывает deep-link бота в новой вкладке + запускает polling профиля,
 * чтобы UI автоматически обновился, как только привязка зафиксирована в БД
 * (handler бота вызывает `/auth/telegram/verify`, AuthService обновляет login).
 * Таймаут 2 мин — дольше polling'а нет смысла.
 *
 * Чистит interval при unmount компонента — иначе polling продолжается фоном
 * после ухода со страницы (нагрузка на ProfileService без надобности).
 *
 * <p>Опциональный <c>onLinked</c> вызывается когда привязка зафиксирована — caller
 * может, например, инвалидировать сторонние queries (status плана в onboarding wizard'е).</p>
 */
export function useTelegramLink(options: { onLinked?: () => void } = {}) {
  const queryClient = useQueryClient();
  const intervalRef = useRef<number | null>(null);
  const onLinkedRef = useRef(options.onLinked);
  // useEffect — потому что обновление ref'ов в render-фазе бьёт react-compiler.
  useEffect(() => {
    onLinkedRef.current = options.onLinked;
  }, [options.onLinked]);

  const stopPolling = () => {
    if (intervalRef.current !== null) {
      window.clearInterval(intervalRef.current);
      intervalRef.current = null;
    }
  };

  // Cleanup on unmount.
  useEffect(() => stopPolling, []);

  const startPolling = () => {
    // Если уже идёт polling от предыдущего клика — заменяем.
    stopPolling();

    const profileKey = profileQueryOptions.getMyProfileKey();
    const startedAt = Date.now();

    const tick = () => {
      void queryClient.refetchQueries({ queryKey: profileKey, type: "active" }).then(() => {
        const profile = queryClient.getQueryData<Envelope<MyProfile>>(profileKey);
        if (profile?.result?.hasTelegramLinked) {
          stopPolling();
          // Backend auto-enables telegramEnabled on link; refetch so the toggle reflects it.
          void queryClient.invalidateQueries({
            queryKey: notificationQueryOptions.preferencesKey(),
          });
          onLinkedRef.current?.();
          toast.success("Telegram привязан");
          return;
        }

        if (Date.now() - startedAt > POLL_TIMEOUT_MS) {
          stopPolling();
        }
      });
    };

    intervalRef.current = window.setInterval(tick, POLL_INTERVAL_MS);
  };

  const mutation = useMutation({
    mutationFn: usersAdminApi.getTelegramLinkToken,
    onSuccess: (data) => {
      window.open(data.deepLinkUrl, "_blank", "noopener,noreferrer");
      toast.success("Нажмите Start в открывшемся боте", {
        description: "Страница автоматически обновится после привязки",
      });
      startPolling();
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка получения ссылки на Telegram"));
    },
  });

  return {
    linkTelegram: mutation.mutate,
    isPending: mutation.isPending,
  };
}
