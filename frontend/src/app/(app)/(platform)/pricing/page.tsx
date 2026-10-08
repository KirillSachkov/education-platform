import type { Metadata } from "next";
import type { PublicPlanDto } from "@/entities/access-plan";
import { PricingCatalog } from "@/widgets/pricing-catalog";
import { APP_URL } from "@/shared/config/site";
import { SiteFooter } from "@/widgets/site-footer";

/**
 * SSR-runtime URL: внутри Docker frontend-контейнера API_URL_INTERNAL
 * указывает на nginx. Локально (npm run dev) fallback на NEXT_PUBLIC_API_URL.
 */
function resolveBaseUrl(): string {
  return process.env.API_URL_INTERNAL ?? process.env.NEXT_PUBLIC_API_URL ?? "http://localhost/api";
}

/**
 * SSR pre-fetch публичных планов платформы. Возвращает данные сразу, без skeleton'а
 * на клиенте. Кешируется на 60 сек — планы меняются редко, но не статика
 * (автор может опубликовать/архивировать через UI).
 */
async function prefetchPublicPlans(): Promise<PublicPlanDto[] | undefined> {
  try {
    const res = await fetch(`${resolveBaseUrl()}/access/plans/public/`, {
      next: { revalidate: 60 },
      signal: AbortSignal.timeout(5_000),
    });
    if (!res.ok) return undefined;
    const json = (await res.json()) as { result?: PublicPlanDto[] };
    return json.result ?? [];
  } catch {
    return undefined;
  }
}

const PRICING_DESCRIPTION =
  "Планы обучения .NET Fullstack в SachkovLearn: курсы, задания, код-ревью и сообщество. Без подписок, доступ навсегда.";

export async function generateMetadata(): Promise<Metadata> {
  const url = `${APP_URL}/pricing`;
  return {
    title: "Доступ — SachkovLearn",
    description: PRICING_DESCRIPTION,
    alternates: { canonical: url },
    openGraph: {
      title: "Доступ — SachkovLearn",
      description: PRICING_DESCRIPTION,
      type: "website",
      url,
    },
    twitter: {
      card: "summary_large_image",
      title: "Доступ — SachkovLearn",
      description: PRICING_DESCRIPTION,
    },
  };
}

/**
 * `/pricing` — страница оплаты (single-tenant flat URL).
 * Доступна гостям (для воронки конверсии); для оплаты редиректит на /login.
 */
export default async function SpacePricingPage() {
  // Server pre-fetch — отдаём как initialData в PricingCatalog. Без skeleton'а на клиенте.
  const initialPlans = await prefetchPublicPlans();
  return (
    <>
      <PricingCatalog initialPlans={initialPlans} />
      <SiteFooter />
    </>
  );
}
