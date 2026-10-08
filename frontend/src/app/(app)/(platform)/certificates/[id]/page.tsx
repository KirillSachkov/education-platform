import type { Metadata } from "next";
import { notFound } from "next/navigation";
import type { CourseCertificateDto } from "@/entities/certificate";
import { CertificateDownloadButtons } from "@/features/download-certificate";
import { routes } from "@/shared/config/routes";
import { formatFullDate } from "@/shared/lib/date";
import { buildEntityMetadata, buildFallbackOgImageUrl, fetchAnonymous } from "@/shared/seo";
import { ShareButton } from "@/shared/ui/components";
import { Icons } from "@/shared/ui/icons";

interface Props {
  params: Promise<{ id: string }>;
}

export async function generateMetadata({ params }: Props): Promise<Metadata> {
  const { id } = await params;
  const certificate = await fetchAnonymous<CourseCertificateDto>(`/progress/certificates/${id}/`);
  if (!certificate) return { title: "Сертификат" };

  const title = `Сертификат: ${certificate.courseTitle}`;
  return buildEntityMetadata({
    title,
    description: `${certificate.holderName} — сертификат о прохождении курса «${certificate.courseTitle}» на SachkovLearn`,
    imageUrl: buildFallbackOgImageUrl(title, certificate.holderName),
    path: routes.certificates(id),
    type: "website",
  });
}

/**
 * Публичная страница проверки сертификата (#467). Работает для анонимов —
 * бэкенд-эндпоинт AllowAnonymous и отдаёт только снапшоты (имя, курс, дата,
 * серийник), поэтому сертификат остаётся проверяемым даже после удаления курса.
 * Повторный fetch того же эндпоинта, что в generateMetadata, дедупится
 * Next.js Data Cache (revalidate 300).
 */
export default async function CertificatePage({ params }: Props) {
  const { id } = await params;
  const certificate = await fetchAnonymous<CourseCertificateDto>(`/progress/certificates/${id}/`);
  if (!certificate) notFound();

  return (
    <div className="mx-auto w-full max-w-2xl px-4 py-8 md:py-14">
      {/* Градиентная рамка: p-px обёртка поверх настоящего border-а карточки. */}
      <div className="rounded-2xl bg-gradient-to-br from-primary/40 via-border to-primary/40 p-px shadow-sm">
        {/* border-transparent — невидим в обычных темах, но проявляется в forced-colors. */}
        <article className="rounded-[calc(1rem-1px)] border border-transparent bg-card px-6 py-10 sm:px-12 sm:py-14">
          <div className="flex flex-col items-center gap-6 text-center">
            <div className="flex size-14 items-center justify-center rounded-full border border-primary/20 bg-primary/10">
              <Icons.graduation className="size-7 text-primary" aria-hidden />
            </div>

            <div className="space-y-1.5">
              <p className="text-xs font-semibold uppercase tracking-[0.25em] text-muted-foreground">
                SachkovLearn
              </p>
              <h1 className="text-sm text-muted-foreground">Сертификат о прохождении</h1>
            </div>

            <p className="text-balance text-3xl font-bold tracking-tight sm:text-4xl">
              {certificate.holderName}
            </p>

            <div className="space-y-2">
              <p className="text-sm text-muted-foreground">успешно прошёл(а) курс</p>
              <p className="text-balance text-xl font-semibold sm:text-2xl">
                «{certificate.courseTitle}»
              </p>
            </div>

            <div className="h-px w-24 bg-border" aria-hidden />

            <div className="space-y-1 text-sm text-muted-foreground">
              <p>Выдан {formatFullDate(certificate.issuedAt)}</p>
              <p className="font-mono text-xs tabular-nums">{certificate.serialNumber}</p>
            </div>

            <p className="flex items-center gap-1.5 text-xs text-muted-foreground">
              <Icons.shieldCheck className="size-3.5 text-primary" aria-hidden />
              Проверено платформой
            </p>

            {/* Дисклеймер по оферте, п.3.3 (152-ФЗ / 273-ФЗ) — сертификат информационный. */}
            <p className="max-w-md text-balance text-[11px] leading-relaxed text-muted-foreground/70">
              Сертификат имеет исключительно информационный характер, подтверждает факт
              прохождения курса и не является документом об образовании или о квалификации.
            </p>
          </div>
        </article>
      </div>

      <div className="mt-6 flex flex-wrap items-center justify-center gap-2">
        <ShareButton
          url={routes.certificates(certificate.id)}
          title={`Сертификат: ${certificate.courseTitle}`}
          text={`${certificate.holderName} — сертификат о прохождении курса «${certificate.courseTitle}»`}
        />
        <CertificateDownloadButtons certificate={certificate} />
      </div>
    </div>
  );
}
