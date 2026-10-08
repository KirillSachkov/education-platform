"use client";

import { Button } from "@/shared/ui/kit/button";
import { AlertCircle, ArrowLeft } from "lucide-react";

function resolveRedirect(next: string | null): {
  href: string;
  isGithubLinkFailed: boolean;
} {
  if (!next) {
    return { href: "/login", isGithubLinkFailed: false };
  }

  try {
    const url = new URL(next, window.location.origin);

    if (url.origin !== window.location.origin) {
      return { href: "/login", isGithubLinkFailed: false };
    }

    const isGithubProfileLink =
      url.pathname === "/profile" &&
      url.searchParams.get("account") === "github-linked";

    if (isGithubProfileLink) {
      url.searchParams.set("account", "github-link-failed");
    }

    return {
      href: url.pathname + url.search,
      isGithubLinkFailed: isGithubProfileLink,
    };
  } catch {
    return { href: "/login", isGithubLinkFailed: false };
  }
}

type Props = {
  next: string | null;
};

export function AuthErrorContent({ next }: Props) {
  const redirect = resolveRedirect(next);

  const href = redirect?.href ?? "/login";
  const message = redirect?.isGithubLinkFailed
    ? "Не удалось привязать GitHub. Возможно, этот аккаунт уже привязан к другому пользователю."
    : "Произошла ошибка при выполнении действия. Вернитесь на платформу и попробуйте снова.";

  return (
    <div className="w-full space-y-4">
      <div className="flex flex-col items-center gap-3 rounded-xl border border-red-500/20 bg-red-500/10 px-4 py-5">
        <AlertCircle className="size-8 text-red-500" />
        <p className="text-sm text-foreground">{message}</p>
      </div>

      <Button asChild variant="default" size="lg" className="w-full gap-2">
        <a href={href}>
          <ArrowLeft className="size-4" />
          Вернуться на платформу
        </a>
      </Button>
    </div>
  );
}
