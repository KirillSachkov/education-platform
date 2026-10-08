"use client";

import { useIsAuthenticated } from "@/shared/auth";
import { routes } from "@/shared/config/routes";
import { useRouter } from "next/navigation";
import { toast } from "sonner";

/**
 * Гейт действий тренажёра для анонима (#614 F). Хаб `/trainer` просматривается
 * read-only без входа, но ЛЮБОЕ действие (старт сессии, ответ, закладка, тренировка,
 * «доучить») требует логина. Хук возвращает `requireAuth`: вызывай в начале
 * обработчика — если аноним, он показывает подсказку и ведёт на `/login` с возвратом
 * на `/trainer`, и возвращает `false` (действие прерываем). Залогинен → `true`.
 */
export function useTrainerLoginGate() {
  const isAuthenticated = useIsAuthenticated();
  const router = useRouter();

  /**
   * Возвращает `true`, если можно продолжать (юзер залогинен). Для анонима показывает
   * toast и редиректит на логин (callbackUrl=/trainer), возвращая `false`.
   */
  const requireAuth = (): boolean => {
    if (isAuthenticated) return true;
    toast.info("Войдите, чтобы начать");
    router.push(`${routes.login}?callbackUrl=${encodeURIComponent(routes.trainer)}`);
    return false;
  };

  return { isAuthenticated, requireAuth };
}
