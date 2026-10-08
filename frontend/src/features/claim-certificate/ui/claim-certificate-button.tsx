"use client";

import { cn } from "@/shared/lib/css";
import { Icons } from "@/shared/ui/icons";
import { Button } from "@/shared/ui/kit/button";
import { useClaimCertificate } from "../model/use-claim-certificate";

interface ClaimCertificateButtonProps {
  courseId: string;
  className?: string;
}

/**
 * Кнопка «Получить сертификат» — показывается на странице прогресса курса при
 * 100% завершении. После claim'а редиректит на публичную страницу сертификата.
 */
export function ClaimCertificateButton({ courseId, className }: ClaimCertificateButtonProps) {
  const { mutate, isPending } = useClaimCertificate(courseId);

  return (
    <Button
      type="button"
      onClick={() => mutate()}
      disabled={isPending}
      className={cn("gap-1.5", className)}
    >
      {isPending ? (
        <Icons.loading className="size-4 animate-spin" />
      ) : (
        <Icons.graduation className="size-4" />
      )}
      Получить сертификат
    </Button>
  );
}
