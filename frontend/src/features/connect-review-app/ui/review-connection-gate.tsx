"use client";

import { getErrorMessage, unwrapEnvelope } from "@/shared/api";
import { AUTH_ORIGIN } from "@/shared/config";
import { Icons } from "@/shared/ui/icons";
import { Button } from "@/shared/ui/kit/button";
import { Card } from "@/shared/ui/kit/card";
import { toast } from "sonner";
import { useStartReviewAppInstallation } from "../model/use-start-installation";

type Props = {
  /** Привязан ли GitHub-аккаунт (шаг 1). Прокидывается из профиля родителем. */
  hasGitHubLinked: boolean;
  requiresGithubConnection?: boolean;
  requiresReviewApp?: boolean;
};

/**
 * Блок «подключи GitHub, чтобы сдать решение». Рендерится на странице задачи
 * ВМЕСТО поля ввода PR + кнопки «Отправить», пока студент не (1) привязал
 * GitHub-аккаунт и (2) установил review-app на свой аккаунт с учебными репо.
 *
 * Гейт показывается только когда подключение НЕ завершено — поэтому достаточно
 * одного признака `hasGitHubLinked`, чтобы выбрать шаг: нет привязки → шаг 1,
 * есть привязка но гейт всё ещё виден → не хватает установки → шаг 2.
 */
export function ReviewConnectionGate({
  hasGitHubLinked,
  requiresGithubConnection = true,
  requiresReviewApp = true,
}: Props) {
  const start = useStartReviewAppInstallation();
  const shouldLinkGithub = requiresGithubConnection && !hasGitHubLinked;
  const requiredConnectionLabel =
    requiresGithubConnection && requiresReviewApp
      ? "GitHub и GitHub App"
      : requiresGithubConnection
        ? "GitHub"
        : "GitHub App";
  const title = requiresReviewApp && !requiresGithubConnection
    ? "Установи GitHub App, чтобы сдать решение"
    : "Подключи GitHub, чтобы сдать решение";

  const handleInstall = () => {
    // returnUrl — текущая страница задачи (относительный путь), чтобы после
    // установки GitHub вернул студента обратно. window доступен — это click.
    const returnUrl = window.location.pathname + window.location.search;
    start.mutate(
      { returnUrl },
      {
        onSuccess: (envelope) => {
          window.location.assign(unwrapEnvelope(envelope).redirectUrl);
        },
        onError: (e) => toast.error(getErrorMessage(e, "Не удалось открыть установку GitHub App")),
      },
    );
  };

  return (
    <Card className="space-y-3 border-amber-500/40 bg-amber-500/5 p-4">
      <div className="flex items-center gap-2">
        <Icons.github className="size-5 shrink-0" />
        <p className="text-sm font-medium">{title}</p>
      </div>
      <p className="text-sm text-muted-foreground">
        Решения сдаются ссылкой на pull request. Для этой задачи автор включил
        обязательное подключение {requiredConnectionLabel}.
      </p>

      {shouldLinkGithub ? (
        <div className="space-y-1.5">
          <Button asChild>
            <a href={`${AUTH_ORIGIN}/auth/github/link`}>
              <Icons.github className="size-4" />
              Привязать GitHub
            </a>
          </Button>
          <p className="text-xs text-muted-foreground">
            {requiresReviewApp
              ? "Шаг 1 из 2. После привязки попросим установить приложение."
              : "После привязки можно будет отправить pull request."}
          </p>
        </div>
      ) : requiresReviewApp ? (
        <div className="space-y-1.5">
          <Button onClick={handleInstall} disabled={start.isPending}>
            <Icons.github className="size-4" />
            {start.isPending ? "Открываем GitHub…" : "Установить GitHub App"}
          </Button>
          <p className="text-xs text-muted-foreground">
            {requiresGithubConnection
              ? "Шаг 2 из 2. На GitHub выбери аккаунт и репозитории со своими проектами."
              : "На GitHub выбери аккаунт и репозитории со своими проектами."}
          </p>
        </div>
      ) : null}
    </Card>
  );
}
