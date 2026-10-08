"use client";

import { useQueryClient } from "@tanstack/react-query";
import type { ComponentProps } from "react";
import { useIsAuthenticated } from "@/shared/auth";
import { ShareButton } from "@/shared/ui/components";
import { materialShortLinkQueryOptions } from "../api";
import type { MaterialId } from "../types";

type MaterialShareButtonProps = Omit<ComponentProps<typeof ShareButton>, "resolveUrl"> & {
  materialId: MaterialId;
};

/**
 * Share-кнопка материала (#507): для залогиненного get-or-create'ит короткий
 * код (`POST /short-links/materials/{id}/`) и шарит `/s/{code}` вместо длинного
 * URL. Код кешируется в React Query (staleTime: Infinity) — повторные клики не
 * делают повторный POST. Аноним — без API-вызова, как раньше; любой сбой
 * шортлинка (429/500) молча фолбэчится на длинный `url` внутри ShareButton.
 */
export function MaterialShareButton({ materialId, ...shareProps }: MaterialShareButtonProps) {
  const isAuthenticated = useIsAuthenticated();
  const queryClient = useQueryClient();

  const resolveShortUrl = async (): Promise<string | null> => {
    const code = await queryClient.fetchQuery(materialShortLinkQueryOptions(materialId));
    return code ? `/s/${code}` : null;
  };

  return <ShareButton {...shareProps} resolveUrl={isAuthenticated ? resolveShortUrl : undefined} />;
}
