import type { Metadata } from "next";
import Link from "next/link";
import { CourseCatalog } from "@/features/course-catalog";
import { routes } from "@/shared/config/routes";
import { APP_URL, toAbsoluteUrl } from "@/shared/config/site";
import { ItemListJsonLd } from "@/shared/seo";

export const metadata: Metadata = {
  // Keyword-tuned title (template appends "| SachkovLearn"). The catalog is the
  // primary commercial SEO target for «курсы C# / .NET / ASP.NET Core».
  title: "Курсы C#, .NET и ASP.NET Core",
  description:
    "Курсы по C#, .NET и ASP.NET Core с AI-ревью PR: backend, микросервисы, " +
    "DevOps и fullstack-разработка — от уверенного junior до Software Engineer.",
  alternates: {
    canonical: toAbsoluteUrl(routes.courses),
  },
  openGraph: {
    title: "Курсы C#, .NET и ASP.NET Core — SachkovLearn",
    description:
      "Курсы по C#, .NET и ASP.NET Core с AI-ревью PR: backend, микросервисы, " +
      "DevOps и fullstack-разработка.",
    type: "website",
    url: toAbsoluteUrl(routes.courses),
    siteName: "SachkovLearn",
    locale: "ru_RU",
  },
};

// SSR-runtime URL: внутри Docker frontend-контейнера API_URL_INTERNAL указывает
// на nginx; локально (npm run dev) — fallback на NEXT_PUBLIC_API_URL. Тот же
// паттерн, что в /pricing и sitemap.ts.
const apiUrl =
  process.env.API_URL_INTERNAL ?? process.env.NEXT_PUBLIC_API_URL ?? "http://localhost/api";

// Render at request time, not at `next build`. The catalog below is fetched from
// the API, which is unreachable during the Docker build (`apiUrl` falls back to
// localhost) — a statically prerendered page bakes in an empty catalog, so the
// crawlable <nav> is dropped and prod keeps serving that cached empty HTML
// (x-nextjs-prerender:1, x-nextjs-cache HIT — regression confirmed: /courses had
// no /courses/<slug> links even after deploy). Forcing dynamic rendering moves
// generation into the running container where API_URL_INTERNAL resolves, so the
// links are in the SSR HTML immediately after deploy. Mirrors sitemap.ts (#450).
export const dynamic = "force-dynamic";

/**
 * SSR-prefetch списка курсов для crawlable-навигации.
 *
 * Интерактивный каталог (<CourseCatalog/>) — client-компонент: список тянется
 * на клиенте через react-query, поэтому ссылки на лендинги отсутствуют в
 * исходном SSR-HTML и невидимы краулерам без JS-рендера (особенно Yandex).
 * Отдаём отдельный server-rendered <nav> со ссылками /courses/<slug>, чтобы
 * каждый лендинг был достижим из каталога. Страница рендерится динамически
 * (см. `export const dynamic` выше), список наполняется на каждом запросе в
 * рантайме, где API доступен. При недоступном API — graceful: nav не рендерится.
 */
async function fetchCatalogLinks(): Promise<{ slug: string; title: string }[]> {
  try {
    const res = await fetch(`${apiUrl}/courses/catalog/?limit=100`, {
      next: { revalidate: 3600 },
    });
    if (!res.ok) return [];
    const json = (await res.json()) as {
      result?: { items?: { slug: string; title: string }[] };
    };
    return json.result?.items ?? [];
  } catch {
    return [];
  }
}

export default async function CoursesPage() {
  const courseLinks = await fetchCatalogLinks();

  return (
    <>
      {courseLinks.length > 0 && (
        <ItemListJsonLd
          items={courseLinks.map((course) => ({
            name: course.title,
            url: `${APP_URL}${routes.courseOverview(course.slug)}`,
          }))}
        />
      )}

      <CourseCatalog />

      {courseLinks.length > 0 && (
        <nav aria-label="Все курсы" className="sr-only">
          <ul>
            {courseLinks.map((course) => (
              <li key={course.slug}>
                <Link href={routes.courseOverview(course.slug)} prefetch={false}>
                  {course.title}
                </Link>
              </li>
            ))}
          </ul>
        </nav>
      )}
    </>
  );
}
