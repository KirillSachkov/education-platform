"use client";

import {
  myInstallationsQueryOptions,
  vcsInstallationApi,
  type VcsInstallation,
} from "@/entities/vcs-installation";
import { getErrorMessage, unwrapEnvelope } from "@/shared/api";
import { Icons } from "@/shared/ui/icons";
import { Button } from "@/shared/ui/kit/button";
import { Card } from "@/shared/ui/kit/card";
import { useMutation, useQuery } from "@tanstack/react-query";
import { toast } from "sonner";

/**
 *  GITHUB_REVIEW_APP step (issue #307). Студент устанавливает AssignmentReviewService
 *  GitHub App на свой аккаунт/org с репозиториями студенческих проектов, чтобы AI
 *  оставлял inline-комментарии на pull request'ах перед ручным ревью.
 *
 *  Auto-complete: на завершении install-flow ARS публикует `vcs_installation.created`,
 *  AccessService.VcsInstallationCreatedHandler (issue #307) переводит этот шаг
 *  в `completed_step_ids[]` всех active onboardings юзера. Поэтому wizard refetch'нет
 *  `/access/onboarding/current/` и продвинется автоматически — кнопки «Завершить»
 *  на этом шаге нет.
 */
export function GithubReviewAppStepView() {
  const installations = useQuery({
    ...myInstallationsQueryOptions(),
    refetchOnMount: "always",
  });

  const start = useMutation({
    mutationFn: vcsInstallationApi.startInstallation,
    onSuccess: (envelope) => {
      const redirectUrl = unwrapEnvelope(envelope).redirectUrl;
      // Full-page redirect на github.com/apps/{slug}/installations/new. После
      // подтверждения GitHub отправит юзера на ARS callback, который сам
      // редиректит обратно на returnUrl (тот pathname, что мы передали).
      window.location.assign(redirectUrl);
    },
    onError: (e) => toast.error(getErrorMessage(e, "Не удалось открыть установку GitHub App")),
  });

  if (installations.isPending) {
    return <div className="text-muted-foreground">Загрузка…</div>;
  }

  if (installations.isError || !installations.data) {
    return (
      <Card className="space-y-2 border-amber-500/40 bg-amber-500/5 p-4 text-sm">
        <div className="font-medium">Не удалось загрузить статус GitHub App.</div>
        <p className="text-muted-foreground">
          Попробуй обновить страницу. Если ошибка повторится — пропусти шаг и подключи App
          позже в «Настройки → Интеграции».
        </p>
      </Card>
    );
  }

  const list = unwrapEnvelope(installations.data).installations;
  const activeInstallation = list.find((i) => i.status === "ACTIVE");
  const suspendedCount = list.filter((i) => i.status === "SUSPENDED").length;

  const handleConnect = () => {
    start.mutate({ returnUrl: window.location.pathname + window.location.search });
  };

  return (
    <div className="space-y-4">
      <div className="flex items-center gap-3">
        <Icons.github className="h-8 w-8" />
        <h2 className="text-2xl font-semibold">AI-проверка PR&apos;ов</h2>
      </div>
      <p className="text-muted-foreground">
        Установи GitHub App на аккаунт или организацию, где живут репозитории
        учебных проектов. AI будет автоматически проверять твои pull request&apos;ы
        и оставлять inline-комментарии до того, как их откроет автор.
      </p>

      {activeInstallation ? (
        <ActiveInstallationCard installation={activeInstallation} />
      ) : (
        <Card className="space-y-3 p-4">
          <p className="text-sm text-muted-foreground">
            Нужно один раз дать приложению доступ к нужным репозиториям.
            На GitHub можно выбрать «Все репозитории» или конкретный список —
            мы будем работать только с тем, что выберешь.
          </p>
          <Button onClick={handleConnect} disabled={start.isPending}>
            <Icons.github className="h-4 w-4" />
            {start.isPending ? "Открываем GitHub…" : "Подключить GitHub App"}
          </Button>
          {list.length > 0 && (
            <p className="text-xs text-muted-foreground">
              {suspendedCount > 0
                ? `${suspendedCount === 1 ? "Предыдущая установка приостановлена" : `${suspendedCount} установки приостановлены`} — переустанови, чтобы возобновить доступ.`
                : "Предыдущая установка удалена в GitHub — нужно установить заново."}
            </p>
          )}
        </Card>
      )}
    </div>
  );
}

function ActiveInstallationCard({ installation }: { installation: VcsInstallation }) {
  return (
    <Card className="flex items-start gap-3 border-green-500/40 bg-green-500/5 p-4 text-sm">
      <Icons.check className="size-4 shrink-0 text-emerald-500" />
      <div className="min-w-0 space-y-1">
        <p>
          GitHub App установлен на{" "}
          <span className="font-mono font-semibold">{installation.ownerLogin}</span> (
          {installation.ownerType === "ORG" ? "организация" : "пользователь"}).
        </p>
        <p className="text-xs text-muted-foreground">
          {installation.allRepos
            ? "Доступ ко всем репозиториям."
            : installation.repos.length === 0
              ? "Без выбранных репозиториев — позже выбери нужные в GitHub Settings → Applications."
              : `Доступ к ${installation.repos.length} репо: ${installation.repos
                  .slice(0, 3)
                  .join(", ")}${installation.repos.length > 3 ? "…" : ""}`}
        </p>
      </div>
    </Card>
  );
}
