"use client";

import { useQuery } from "@tanstack/react-query";
import {
  type AdminVcsInstallation,
  type AdminVcsInstallationStatus,
  adminCrossServiceQueryOptions,
} from "@/entities/admin-cross-service";
import { pluralize } from "@/shared/lib/pluralize";
import { Badge } from "@/shared/ui/kit/badge";
import { Card, CardContent } from "@/shared/ui/kit/card";
import { Icons } from "@/shared/ui/icons";
import { Skeleton } from "@/shared/ui/kit/skeleton";

type GithubStatusBlockProps = {
  userId: string;
};

/**
 * GitHub/App статус юзера для post-purchase панели (#444): GitHub-привязка
 * (linked + @username/id + cached orgs, AuthService) и GitHub App installation'ы
 * AI-проверки PR'ов (AssignmentReviewService). Закрывает последний пункт критериев
 * Phase 4 «support/admin без SQL видит … GitHub/App status». Два независимых query,
 * каждый soft-degrade'ит на ошибке (как `TelegramIdentityBlock`).
 */
export function GithubStatusBlock({ userId }: GithubStatusBlockProps) {
  const github = useQuery(adminCrossServiceQueryOptions.getUserGithubStatusOptions(userId));
  const installations = useQuery(
    adminCrossServiceQueryOptions.getUserVcsInstallationsOptions(userId),
  );

  return (
    <Card>
      <CardContent className="space-y-4 p-4">
        {/* GitHub identity */}
        <div className="flex flex-wrap items-center gap-x-4 gap-y-2">
          <div className="flex items-center gap-2">
            <span className="flex size-7 shrink-0 items-center justify-center rounded-lg bg-foreground/5 text-foreground">
              <Icons.github className="size-3.5" />
            </span>
            <span className="text-sm font-medium">GitHub</span>
          </div>

          {github.isLoading ? (
            <Skeleton className="h-5 w-40" />
          ) : github.isError || !github.data ? (
            <span className="text-xs text-muted-foreground">Не удалось загрузить</span>
          ) : github.data.linked ? (
            <div className="flex min-w-0 flex-wrap items-center gap-x-3 gap-y-1 text-sm">
              <Badge variant="secondary" className="text-[11px]">
                Привязан
              </Badge>
              {github.data.githubUsername ? (
                <span className="text-foreground">@{github.data.githubUsername}</span>
              ) : null}
              {github.data.githubUserId ? (
                <code className="text-xs text-muted-foreground">id {github.data.githubUserId}</code>
              ) : null}
            </div>
          ) : (
            <Badge variant="outline" className="text-[11px]">
              Не привязан
            </Badge>
          )}
        </div>

        {/* GitHub orgs */}
        {github.data?.linked && github.data.orgs.length > 0 ? (
          <div className="flex flex-wrap items-center gap-1.5 border-t border-border/40 pt-3">
            <span className="text-xs font-medium text-foreground">Организации:</span>
            {github.data.orgs.map((org) => (
              <Badge key={org.slug} variant="outline" className="max-w-full text-[11px]">
                <span className="truncate">{org.slug}</span>
              </Badge>
            ))}
          </div>
        ) : null}

        {/* GitHub App (AI-review) installations */}
        <div className="space-y-2 border-t border-border/40 pt-3">
          <p className="text-xs font-medium text-foreground">GitHub App (AI-проверка PR’ов)</p>
          {installations.isLoading ? (
            <Skeleton className="h-8 w-full" />
          ) : installations.isError ? (
            <p className="text-xs text-muted-foreground">Не удалось загрузить установки App</p>
          ) : (installations.data ?? []).length === 0 ? (
            <p className="text-xs text-muted-foreground">GitHub App не подключён</p>
          ) : (
            <ul className="space-y-2">
              {(installations.data ?? []).map((install) => (
                <InstallationRow key={install.id} install={install} />
              ))}
            </ul>
          )}
        </div>
      </CardContent>
    </Card>
  );
}

const INSTALL_STATUS_LABEL: Record<AdminVcsInstallationStatus, string> = {
  ACTIVE: "активна",
  SUSPENDED: "приостановлена",
  UNINSTALLED: "удалена",
};

function InstallationRow({ install }: { install: AdminVcsInstallation }) {
  const statusVariant: "secondary" | "outline" | "destructive" =
    install.status === "ACTIVE"
      ? "secondary"
      : install.status === "SUSPENDED"
        ? "outline"
        : "destructive";

  return (
    <li className="flex flex-wrap items-center justify-between gap-2 rounded-md border border-border/50 px-3 py-2">
      <div className="flex min-w-0 flex-wrap items-center gap-x-2 gap-y-1">
        <span className="min-w-0 truncate text-sm font-medium">{install.ownerLogin}</span>
        <Badge variant="outline" className="text-[11px]">
          {install.ownerType === "ORG" ? "организация" : "аккаунт"}
        </Badge>
        <Badge variant={statusVariant} className="text-[11px]">
          {INSTALL_STATUS_LABEL[install.status]}
        </Badge>
      </div>
      <span className="text-xs text-muted-foreground">
        {install.allRepos
          ? "все репозитории"
          : `${install.repos.length} ${pluralize(install.repos.length, "репозиторий", "репозитория", "репозиториев")}`}
      </span>
    </li>
  );
}
