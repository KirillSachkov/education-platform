"use client";

import { toast } from "sonner";
import { Icons } from "@/shared/ui/icons";
import { Button } from "@/shared/ui/kit/button";

type CopyInviteButtonProps = {
  inviteLink: string;
  /** Подпись для tooltip/aria — например «Скопировать ссылку на чат». */
  label?: string;
};

/**
 * Копирует invite-ссылку чата в буфер обмена с toast (#444). Тот же приём, что в
 * `shared/ui/components/share-button` — `navigator.clipboard.writeText` внутри
 * обработчика (secure-context на проде/localhost), без проверок при рендере.
 */
export function CopyInviteButton({ inviteLink, label = "Скопировать ссылку" }: CopyInviteButtonProps) {
  async function handleCopy() {
    try {
      await navigator.clipboard.writeText(inviteLink);
      toast.success("Ссылка скопирована");
    } catch {
      toast.error("Не удалось скопировать ссылку");
    }
  }

  return (
    <Button
      type="button"
      variant="outline"
      size="sm"
      className="min-touch gap-1.5"
      onClick={handleCopy}
      title={label}
      aria-label={label}
    >
      <Icons.copy className="size-3.5" />
      <span className="hidden sm:inline">Скопировать</span>
    </Button>
  );
}
