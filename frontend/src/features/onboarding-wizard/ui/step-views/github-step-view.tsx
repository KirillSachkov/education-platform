"use client";

import {
  type GithubInvitationStatusResponse,
  githubAppApi,
  githubInvitationStatusQueryOptions,
} from "@/entities/plan-onboarding";
import { profileQueryOptions } from "@/entities/profile";
import { getErrorMessage, isEnvelopeError } from "@/shared/api";
import { AUTH_ORIGIN } from "@/shared/config";
import { PRIMARY_AUTHOR_CONSULTATION_LINK } from "@/shared/config/primary-author";
import { Icons } from "@/shared/ui/icons";
import { Button } from "@/shared/ui/kit/button";
import { Card } from "@/shared/ui/kit/card";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useSearchParams } from "next/navigation";
import { toast } from "sonner";
import { extractGithubLogin } from "../../model/github-login";

const GITHUB_MEMBERSHIP_REQUIRED_CODE = "onboarding.github.membership.required";
const GITHUB_VERIFICATION_UNAVAILABLE_CODE = "onboarding.github.verification.unavailable";

/** Результат OAuth-привязки из `?account=` — backend возвращает его после callback'а. */
const LINK_RESULT_ERRORS: Record<string, string> = {
  "github-link-conflict":
    "Этот GitHub-аккаунт уже привязан к другому пользователю платформы. Войди под тем аккаунтом и отвяжи GitHub либо привяжи другой GitHub-аккаунт.",
  "github-link-failed": "Не удалось привязать GitHub. Попробуй ещё раз.",
  "github-already-linked":
    "К платформе привязан другой GitHub-аккаунт. Авторизуйся на GitHub тем аккаунтом, который привязывал раньше, или отвяжи его в настройках и привяжи заново.",
};

function hasErrorCode(error: unknown, code: string): boolean {
  return isEnvelopeError(error) && error.messages.some((m) => m.code === code);
}

type Props = {
  planId: string;
  planGitHubOrgSlug: string | null;
  /**
   * Ошибка последней попытки «Далее» (footer wizard'а → completeStep). Прокидывается
   * сверху, чтобы GITHUB-шаг показал inline-обратную связь, когда mandatory-проверка
   * членства отклонила завершение. Без этого единственным каналом ошибки оставался
   * sonner-тост, который в полноэкранной modal-онбординга на мобиле перекрывается —
   * и «Далее» казалось «ничего не делает». Зеркалит TELEGRAM-шаг (#447). Подтверждённый
   * член org'а сюда не попадает — у него рендерится зелёная карточка и «Далее» проходит (#448).
   */
  completeError?: unknown;
};

/**
 *  GitHub-step с автоматическим polling статуса invitation. После Accept
 *  в org webhook ставит ACCEPTED → query invalidates → wizard advances.
 *
 *  Если у юзера в `MyProfile.githubOrgs` уже есть org плана — показываем
 *  fast-path «Вы уже состоите» сразу, без отправки повторного invite.
 */
export function GithubStepView({ planId, planGitHubOrgSlug, completeError }: Props) {
  const queryClient = useQueryClient();
  // «Членство не подтверждено» — server-side mandatory-проверка «Далее» вернула 400.
  // Показываем тот же inline-статус, что и кнопки шага, чтобы «Далее» и карточка не расходились.
  const completeRejectedForMembership = hasErrorCode(
    completeError,
    GITHUB_MEMBERSHIP_REQUIRED_CODE,
  );
  // Проверку не удалось выполнить (GitHub / App автора / AuthService недоступны) — это не
  // «ты не в org», поэтому подсказка другая: подождать и повторить (#1148).
  const completeVerificationUnavailable = hasErrorCode(
    completeError,
    GITHUB_VERIFICATION_UNAVAILABLE_CODE,
  );
  const linkResultError = LINK_RESULT_ERRORS[useSearchParams()?.get("account") ?? ""];
  // refetchOnMount: 'always' уже гарантирует свежий профиль при заходе на step —
  // полезно когда юзер только что привязал GitHub в settings.
  const profile = useQuery({
    ...profileQueryOptions.getMyProfileOptions(),
    refetchOnMount: "always",
  });
  const invitation = useQuery({
    ...githubInvitationStatusQueryOptions(planId),
    refetchInterval: (query) => {
      const status = query.state.data?.result?.status;
      return status === "PENDING" ? 5_000 : false;
    },
    refetchOnMount: "always",
  });

  // Привязка — по `hasGitHubLinked` (user_logins), логин — из профиля. Привязанный юзер
  // без логина видит «Обновить привязку», а не «Привяжи GitHub» (#1148).
  const hasGitHubLinked = profile.data?.hasGitHubLinked ?? false;
  const githubLogin = extractGithubLogin(profile.data?.profiles?.student?.gitHubUrl ?? null);

  const create = useMutation({
    mutationFn: (login: string) => githubAppApi.createInvitation({ planId, githubLogin: login }),
    // Backend пере-прогоняет и терминальные строки (#501) — тостим по фактическому
    // итогу, а не «Запрос отправлен» при повторном FAILED.
    onSuccess: async (data) => {
      const status = data.result?.status;
      if (status === "ACCEPTED") toast.success("Членство подтверждено");
      else if (status === "PENDING") toast.success("Приглашение отправлено");
      else toast.info("Автоматическое приглашение пока недоступно");
      await queryClient.invalidateQueries({
        queryKey: ["plan-onboarding", "github-invitation", planId],
      });
    },
    onError: (e) => toast.error(getErrorMessage(e, "Не удалось отправить приглашение")),
  });

  const sync = useMutation({
    mutationFn: (id: string) => githubAppApi.syncInvitation(id),
    onSuccess: async (data) => {
      if (data.result?.status === "ACCEPTED") {
        toast.success("Членство подтверждено");
      } else {
        toast.info("Пока не подтверждено GitHub'ом");
      }
      await queryClient.invalidateQueries({
        queryKey: ["plan-onboarding", "github-invitation", planId],
      });
    },
    onError: (e) => toast.error(getErrorMessage(e, "Не удалось проверить статус")),
  });

  if (profile.isPending || invitation.isPending) {
    return <div className="text-muted-foreground">Загрузка…</div>;
  }

  // GitHub не привязан, либо привязан, но логин не сохранился.
  if (!hasGitHubLinked || !githubLogin) {
    return (
      <div className="space-y-4">
        <div className="flex items-center gap-3">
          <Icons.github className="h-8 w-8" />
          <h2 className="text-2xl font-semibold">
            {hasGitHubLinked ? "Обнови привязку GitHub" : "Привяжи GitHub"}
          </h2>
        </div>
        <p className="text-muted-foreground">
          {hasGitHubLinked
            ? "GitHub привязан, но мы не получили логин аккаунта. Авторизуйся на GitHub тем же аккаунтом ещё раз — данные подтянутся."
            : "Чтобы автор мог пригласить тебя в репозитории, нужен привязанный GitHub-аккаунт."}
        </p>
        {linkResultError && (
          <div
            role="alert"
            className="flex gap-2.5 rounded-lg border border-amber-500/30 bg-amber-500/10 p-3 text-sm"
          >
            <Icons.warning className="mt-0.5 h-4 w-4 shrink-0 text-amber-600 dark:text-amber-500" />
            <p className="text-foreground">{linkResultError}</p>
          </div>
        )}
        <Button asChild>
          <a href={`${AUTH_ORIGIN}/auth/github/link`}>
            <Icons.github className="h-4 w-4" />
            {hasGitHubLinked ? "Обновить привязку" : "Привязать GitHub"}
          </a>
        </Button>
        <p className="text-xs text-muted-foreground">
          После авторизации на GitHub этот шаг откроется снова.
        </p>
      </div>
    );
  }

  // Fast-path: юзер уже состоит в org плана (по cached profile.githubOrgs).
  // Показываем зелёный confirm сразу, без отправки повторного invite.
  const githubOrgs = profile.data?.githubOrgs ?? [];
  const alreadyMember =
    !!planGitHubOrgSlug &&
    githubOrgs.some((o) => o.toLowerCase() === planGitHubOrgSlug.toLowerCase());

  if (alreadyMember) {
    return (
      <div className="space-y-4">
        <div className="flex items-center gap-3">
          <Icons.github className="h-8 w-8" />
          <h2 className="text-2xl font-semibold">Доступ к GitHub-org</h2>
        </div>
        <p className="text-sm text-muted-foreground">
          Привязан как <span className="font-mono font-semibold">@{githubLogin}</span>.
        </p>
        <Card className="flex items-center gap-2 border-green-500/40 bg-green-500/5 p-4 text-sm">
          <Icons.check className="size-4 shrink-0 text-emerald-500" />
          <span>
            Вы состоите в org <span className="font-mono font-semibold">{planGitHubOrgSlug}</span> —
            можно идти дальше.
          </span>
        </Card>
      </div>
    );
  }

  return (
    <div className="space-y-4">
      <div className="flex items-center gap-3">
        <Icons.github className="h-8 w-8" />
        <h2 className="text-2xl font-semibold">Доступ к GitHub-org</h2>
      </div>
      <p className="text-sm text-muted-foreground">
        Привязан как <span className="font-mono font-semibold">@{githubLogin}</span>.
      </p>
      <InvitationCard
        invitation={invitation.data}
        githubLogin={githubLogin}
        isCreating={create.isPending}
        isSyncing={sync.isPending}
        onCreate={() => create.mutate(githubLogin)}
        onSync={(id) => sync.mutate(id)}
        createError={create.error}
      />

      {completeVerificationUnavailable && (
        <div
          role="alert"
          className="flex gap-2.5 rounded-lg border border-amber-500/30 bg-amber-500/10 p-3 text-sm"
        >
          <Icons.warning className="mt-0.5 h-4 w-4 shrink-0 text-amber-600 dark:text-amber-500" />
          <div className="min-w-0 space-y-1">
            <p className="font-medium text-foreground">Не удалось проверить членство</p>
            <p className="text-muted-foreground">
              GitHub или интеграция автора сейчас не отвечают. Попробуй нажать «Далее» через
              несколько минут. Если не проходит — напиши в поддержку.
            </p>
            <a
              href={PRIMARY_AUTHOR_CONSULTATION_LINK}
              target="_blank"
              rel="noopener noreferrer"
              className="inline-flex items-center gap-1 font-medium text-primary hover:underline"
            >
              <Icons.telegram className="h-3.5 w-3.5" />
              Написать в поддержку
            </a>
          </div>
        </div>
      )}

      {completeRejectedForMembership && (
        <div
          role="alert"
          className="flex gap-2.5 rounded-lg border border-amber-500/30 bg-amber-500/10 p-3 text-sm"
        >
          <Icons.warning className="mt-0.5 h-4 w-4 shrink-0 text-amber-600 dark:text-amber-500" />
          <div className="min-w-0 space-y-1">
            <p className="font-medium text-foreground">
              Членство в GitHub-организации ещё не подтверждено
            </p>
            <p className="text-muted-foreground">
              {invitation.data?.status === "PENDING"
                ? "Прими приглашение в организацию на GitHub (кнопка выше), затем нажми «Я уже принял» — и шаг откроется."
                : "Запроси приглашение кнопкой выше и прими его в организации на GitHub."}{" "}
              Если уже принял, а проверка не проходит — напиши в поддержку.
            </p>
            <a
              href={PRIMARY_AUTHOR_CONSULTATION_LINK}
              target="_blank"
              rel="noopener noreferrer"
              className="inline-flex items-center gap-1 font-medium text-primary hover:underline"
            >
              <Icons.telegram className="h-3.5 w-3.5" />
              Написать в поддержку
            </a>
          </div>
        </div>
      )}
    </div>
  );
}

type CardProps = {
  invitation: GithubInvitationStatusResponse | null | undefined;
  githubLogin: string;
  isCreating: boolean;
  isSyncing: boolean;
  onCreate: () => void;
  onSync: (invitationId: string) => void;
  createError: unknown;
};

function InvitationCard({
  invitation,
  githubLogin,
  isCreating,
  isSyncing,
  onCreate,
  onSync,
  createError,
}: CardProps) {
  if (!invitation) {
    // У плана нет настроенной GitHub org — backend вернёт 404
    // (`access.plan.github.not.configured`). Показываем дружелюбное сообщение
    // вместо retry-кнопки.
    if (createError && isPlanGithubNotConfigured(createError)) {
      return (
        <Card className="space-y-2 p-4 text-sm">
          <div className="font-medium">У этого плана нет привязки к GitHub-организации.</div>
          <p className="text-muted-foreground">
            Свяжитесь с автором, чтобы он добавил вас вручную, или попросите настроить
            автоматическое приглашение через настройки плана.
          </p>
        </Card>
      );
    }

    return (
      <Card className="space-y-3 p-4">
        <div className="text-sm text-muted-foreground">
          Этот план даёт доступ к репозиториям GitHub. Отправим приглашение для аккаунта{" "}
          <strong>@{githubLogin}</strong>.
        </div>
        <Button onClick={onCreate} disabled={isCreating}>
          Получить приглашение
        </Button>
      </Card>
    );
  }

  switch (invitation.status) {
    case "ACCEPTED":
      return (
        <Card className="border-green-500/40 bg-green-500/5 p-4 text-sm">
          ✓ Ты в org <strong>{invitation.orgLogin}</strong>. Можно идти дальше.
        </Card>
      );
    case "PENDING":
      return (
        <Card className="space-y-3 p-4">
          <div className="text-sm">
            Приглашение отправлено в org <strong>{invitation.orgLogin}</strong>. Зайди на{" "}
            <a
              href={`https://github.com/orgs/${invitation.orgLogin}/invitation`}
              target="_blank"
              rel="noopener noreferrer"
              className="underline"
            >
              github.com
            </a>{" "}
            и подтверди — мы сами увидим.
          </div>
          <Button
            variant="outline"
            size="sm"
            onClick={() => onSync(invitation.id)}
            disabled={isSyncing}
          >
            Я уже принял
          </Button>
        </Card>
      );
    case "FAILED": {
      const reason = invitation.failureReason;
      // no_installation — на момент попытки у автора не был установлен GitHub App.
      // Backend пере-прогоняет FAILED-строки (#501): если автор с тех пор установил App
      // или добавил юзера в org вручную — повторная попытка чинит статус сама.
      if (reason === "no_installation") {
        return (
          <Card className="space-y-3 border-amber-500/40 bg-amber-500/5 p-4 text-sm">
            <div className="font-medium">Не удалось отправить приглашение автоматически.</div>
            <p className="text-muted-foreground">
              Свяжитесь с автором — попросите добавить <strong>@{githubLogin}</strong> в org{" "}
              <strong>{invitation.orgLogin}</strong> вручную. Если автор уже добавил вас или
              подключил приглашения — проверьте ещё раз.
            </p>
            <Button variant="outline" size="sm" onClick={onCreate} disabled={isCreating}>
              Проверить ещё раз
            </Button>
          </Card>
        );
      }
      // github_user_not_found — GitHub-логин юзера не существует (или не существовал
      // на момент попытки). После смены привязки повторная попытка пере-прогоняет флоу.
      if (reason === "github_user_not_found") {
        return (
          <Card className="space-y-3 border-amber-500/40 bg-amber-500/5 p-4 text-sm">
            <div className="font-medium">
              GitHub-аккаунт <strong>@{githubLogin}</strong> не найден.
            </div>
            <p className="text-muted-foreground">
              Проверьте что аккаунт существует на github.com. Можно отвязать его в настройках
              профиля и привязать другой, затем повторить попытку.
            </p>
            <Button variant="outline" size="sm" onClick={onCreate} disabled={isCreating}>
              Попробовать ещё раз
            </Button>
          </Card>
        );
      }
      return (
        <Card className="space-y-3 border-amber-500/40 bg-amber-500/5 p-4 text-sm">
          <div className="font-medium">Не удалось отправить приглашение.</div>
          <p className="text-muted-foreground">
            Попробуйте ещё раз. Если не получится — попросите автора пригласить вас вручную.
          </p>
          <Button variant="outline" size="sm" onClick={onCreate} disabled={isCreating}>
            Повторить
          </Button>
        </Card>
      );
    }
    case "EXPIRED":
    case "CANCELED":
      return (
        <Card className="space-y-3 p-4 text-sm">
          <div>
            Приглашение {invitation.status === "EXPIRED" ? "истекло" : "отозвано"}. Запроси новое.
          </div>
          <Button variant="outline" size="sm" onClick={onCreate} disabled={isCreating}>
            Запросить новое приглашение
          </Button>
        </Card>
      );
    default:
      return null;
  }
}

/**
 *  Распознаёт backend-ошибку «у плана нет github org configured»: AccessService
 *  возвращает <c>github_app.plan.org.missing</c> (см. <c>GitHubAppErrors.PlanGithubOrgMissing</c>).
 *  Точное сравнение по коду — substring-совпадения ловят `installation.not.found` и т.п.,
 *  что приводит к ложным «свяжитесь с автором» сообщениям.
 */
function isPlanGithubNotConfigured(error: unknown): boolean {
  if (!error || typeof error !== "object") return false;
  const e = error as { response?: { data?: { error?: { messages?: { code?: string }[] } } } };
  const code = e.response?.data?.error?.messages?.[0]?.code ?? "";
  return code === "github_app.plan.org.missing";
}
