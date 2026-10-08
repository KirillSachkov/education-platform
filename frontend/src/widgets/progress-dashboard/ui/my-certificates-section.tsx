"use client";

import Link from "next/link";
import { useQuery } from "@tanstack/react-query";
import { certificateQueryOptions } from "@/entities/certificate";
import { routes } from "@/shared/config/routes";
import { formatShortDate } from "@/shared/lib/date";
import { Icons } from "@/shared/ui/icons";

/**
 * Секция «Мои сертификаты» на /progress — карточки выданных сертификатов со
 * ссылкой на публичную страницу проверки. Пока сертификатов нет, секция
 * полностью скрыта (ничего не рендерит). Issue #467.
 */
export function MyCertificatesSection() {
  const { data: certificates } = useQuery(certificateQueryOptions.myOptions());

  if (!certificates || certificates.length === 0) {
    return null;
  }

  return (
    <section className="space-y-3">
      <h2 className="text-lg font-semibold">Мои сертификаты</h2>
      <div className="grid gap-3 sm:grid-cols-2">
        {certificates.map((certificate) => (
          <Link
            key={certificate.id}
            href={routes.certificates(certificate.id)}
            className="group flex items-start gap-3 rounded-xl border bg-card p-4 transition-colors hover:border-primary/40"
          >
            <div className="flex size-10 shrink-0 items-center justify-center rounded-full border border-primary/20 bg-primary/10">
              <Icons.graduation className="size-5 text-primary" aria-hidden />
            </div>
            <div className="min-w-0 space-y-1">
              <p className="truncate font-medium group-hover:text-primary">
                {certificate.courseTitle}
              </p>
              <p className="text-xs text-muted-foreground">
                {formatShortDate(certificate.issuedAt)} ·{" "}
                <span className="font-mono tabular-nums">{certificate.serialNumber}</span>
              </p>
            </div>
          </Link>
        ))}
      </div>
    </section>
  );
}
