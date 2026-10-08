"use client";

import { useQuery } from "@tanstack/react-query";
import {
  myInstallationsQueryOptions,
  type VcsInstallation,
} from "@/entities/vcs-installation";
import { getErrorMessage, unwrapEnvelope } from "@/shared/api";
import { Icons } from "@/shared/ui/icons";
import { Badge } from "@/shared/ui/kit/badge";
import { Button } from "@/shared/ui/kit/button";
import { Skeleton } from "@/shared/ui/kit/skeleton";
import { toast } from "sonner";
import { useStartReviewAppInstallation } from "../model/use-start-installation";

/**
 * Карточка «AI-проверка PR&apos;ов» в /settings/integrations. Видна и автору, и
 * студенту (см. `IntegrationsSection`) — описание переключается через `isAuthor`.
 * Если установок нет — показывает CTA «Подключить». Если есть — список
 * аккаунтов / организаций с маркером статуса + кнопку «Добавить ещё».
 */
export function ReviewAppCard({ isAuthor = true }: { isAuthor?: boolean }) {
  const { data, isPending, isError } = useQuery(myInstallationsQueryOptions());

  if (isPending) {
    return <Skeleton className="h-24 w-full rounded-2xl" />;
  }

  if (isError || !data) {
    return (
      <CardShell>
        <p className="text-sm text-destructive">
          Не удалось загрузить список GitHub-приложений.
        </p>
      </CardShell>
    );
  }

  const installations = unwrapEnvelope(data).installations;
  const hasAny = installations.length > 0;

  return (
    <CardShell>
      <div className="flex items-start gap-3">
        <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-muted">
          <Icons.github className="h-5 w-5" />
        </div>
        <div className="min-w-0 flex-1 space-y-3">
          <div className="space-y-1">
            <p className="text-sm font-medium">AI-проверка PR&apos;ов</p>
            <p className="text-sm text-muted-foreground">
              {isAuthor
                ? "Установите GitHub App на репозитории студентов, чтобы AI проверял PR перед вашим ревью и оставлял inline-комментарии в pull request."
                : "Установи GitHub App на свой аккаунт с учебными репозиториями, чтобы AI проверял твои pull request'ы и оставлял inline-комментарии до ревью наставника. Без этого сдать решение на проверку не получится."}
            </p>
          </div>

          {hasAny ? (
            <InstallationsList installations={installations} />
          ) : (
            <ConnectButton label="Подключить GitHub App" />
          )}

          {hasAny && <ConnectButton label="Добавить ещё аккаунт / организацию" variant="outline" />}
        </div>
      </div>
    </CardShell>
  );
}

function InstallationsList({ installations }: { installations: VcsInstallation[] }) {
  return (
    <ul className="space-y-2">
      {installations.map((inst) => (
        <li key={inst.id} className="flex items-center justify-between gap-3 rounded-xl border bg-card p-3">
          <div className="min-w-0">
            <p className="truncate text-sm font-medium">
              {inst.ownerLogin}
              <span className="ml-2 text-xs font-normal text-muted-foreground">
                {inst.ownerType === "ORG" ? "организация" : "пользователь"}
              </span>
            </p>
            <p className="truncate text-xs text-muted-foreground">
              {inst.allRepos
                ? "Все репозитории"
                : inst.repos.length === 0
                  ? "Без выбранных репозиториев"
                  : `${inst.repos.length} репозиториев: ${inst.repos.slice(0, 3).join(", ")}${inst.repos.length > 3 ? "…" : ""}`}
            </p>
          </div>
          <StatusBadge status={inst.status} />
        </li>
      ))}
    </ul>
  );
}

function StatusBadge({ status }: { status: VcsInstallation["status"] }) {
  if (status === "ACTIVE") {
    return <Badge variant="secondary">Активно</Badge>;
  }
  if (status === "SUSPENDED") {
    return <Badge variant="destructive">Приостановлено</Badge>;
  }
  return <Badge variant="outline">Удалено</Badge>;
}

function ConnectButton({
  label,
  variant = "default",
}: {
  label: string;
  variant?: "default" | "outline";
}) {
  const { mutate, isPending } = useStartReviewAppInstallation();

  function handleClick() {
    // returnUrl не передаём — backend default'нет на /settings/integrations?installation=success.
    mutate(
      {},
      {
        onSuccess: (envelope) => {
          const redirectUrl = unwrapEnvelope(envelope).redirectUrl;
          window.location.assign(redirectUrl);
        },
        onError: (e) => toast.error(getErrorMessage(e, "Не удалось открыть установку GitHub App")),
      },
    );
  }

  return (
    <Button variant={variant} disabled={isPending} onClick={handleClick}>
      {isPending ? "Открываем GitHub…" : label}
    </Button>
  );
}

function CardShell({ children }: { children: React.ReactNode }) {
  return <div className="rounded-2xl border bg-card p-4">{children}</div>;
}
