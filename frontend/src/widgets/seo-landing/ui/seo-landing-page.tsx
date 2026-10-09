import Link from "next/link";
import { ArrowRight, Check, ChevronDown } from "lucide-react";
import { routes } from "@/shared/config/routes";
import type { SeoLandingContent, SeoLandingCta } from "../model/types";

/**
 * Server-rendered keyword SEO landing (`/c-sharp`, `/dotnet`, `/asp-net-core`).
 *
 * No "use client" on purpose: every word of the unique body copy must be in the
 * initial SSR HTML so Google/Yandex index it without running JS (Yandex JS
 * rendering is weak). The FAQ uses native `<details>/<summary>` disclosure —
 * accessible, JS-free, and keeps the answers in the crawlable markup. The same
 * `content.faq` feeds `FaqJsonLd` at the page level.
 *
 * Visual language mirrors the marketing landing (dark surfaces, teal accent) for
 * brand consistency, but this is a standalone composition — it deliberately does
 * NOT import the conversion-critical flagship sections to avoid coupling/regressions.
 */
const ACCENT = "#6BADA5";

function CtaLink({ cta, variant }: { cta: SeoLandingCta; variant: "primary" | "secondary" }) {
  if (variant === "primary") {
    return (
      <Link
        href={cta.href}
        className="group inline-flex items-center gap-2 rounded-lg bg-[#6BADA5] px-7 py-3.5 text-sm font-medium text-[#0A0A0B] transition-all hover:bg-[#5CEAC9] hover:shadow-[0_0_40px_rgba(107,173,165,0.4)]"
      >
        {cta.label}
        <ArrowRight className="h-4 w-4 transition-transform group-hover:translate-x-0.5" />
      </Link>
    );
  }
  return (
    <Link
      href={cta.href}
      className="inline-flex items-center gap-2 rounded-lg border border-white/[0.1] px-7 py-3.5 text-sm font-medium text-white/70 transition-all hover:border-white/25 hover:text-white"
    >
      {cta.label}
    </Link>
  );
}

export function SeoLandingPage({ content }: { content: SeoLandingContent }) {
  return (
    <div className="min-h-svh bg-[#0A0A0B] text-[#FAFAFA] antialiased selection:bg-[#6BADA5]/30">
      {/* Hero — a `<section>`, not `<header>`: the page's real header is the
          LandingHeader rendered by the (seo) layout. */}
      <section className="mx-auto max-w-4xl px-5 pb-12 pt-20 sm:pt-28">
        <p className="text-xs font-semibold uppercase tracking-[0.18em]" style={{ color: ACCENT }}>
          {content.eyebrow}
        </p>
        <h1 className="mt-4 text-3xl font-extrabold leading-[1.1] tracking-tight sm:text-5xl">
          {content.h1}
        </h1>
        <p className="mt-5 max-w-2xl text-base leading-relaxed text-white/65 sm:text-lg">
          {content.heroLead}
        </p>
        <div className="mt-8 flex flex-col gap-3 sm:flex-row">
          <CtaLink cta={content.heroCtaPrimary} variant="primary" />
          {content.heroCtaSecondary && (
            <CtaLink cta={content.heroCtaSecondary} variant="secondary" />
          )}
        </div>
      </section>

      {/* Body sections */}
      <main className="mx-auto max-w-4xl px-5">
        {content.sections.map((section) => (
          <section
            key={section.heading}
            id={section.id}
            className="border-t border-white/[0.06] py-12"
          >
            <h2 className="text-2xl font-bold tracking-tight sm:text-3xl">{section.heading}</h2>
            <div className="mt-5 space-y-4 text-[15px] leading-relaxed text-white/70 sm:text-base">
              {section.body.map((paragraph) => (
                <p key={paragraph.slice(0, 48)}>{paragraph}</p>
              ))}
            </div>
            {section.bullets && section.bullets.length > 0 && (
              <ul className="mt-6 grid gap-3 sm:grid-cols-2">
                {section.bullets.map((bullet) => (
                  <li key={bullet} className="flex items-start gap-2.5 text-[15px] text-white/75">
                    <Check className="mt-0.5 h-5 w-5 flex-shrink-0" style={{ color: ACCENT }} />
                    <span>{bullet}</span>
                  </li>
                ))}
              </ul>
            )}
          </section>
        ))}

        {/* FAQ — native disclosure, fully in SSR HTML */}
        <section className="border-t border-white/[0.06] py-12">
          <h2 className="text-2xl font-bold tracking-tight sm:text-3xl">Частые вопросы</h2>
          <div className="mt-6 divide-y divide-white/[0.06] border-y border-white/[0.06]">
            {content.faq.map((item) => (
              <details key={item.question} className="group py-5">
                <summary className="flex cursor-pointer list-none items-center justify-between gap-4 text-[15px] font-medium text-white/90 sm:text-base">
                  {item.question}
                  <ChevronDown className="h-5 w-5 flex-shrink-0 text-white/40 transition-transform group-open:rotate-180" />
                </summary>
                <p className="mt-3 text-[15px] leading-relaxed text-white/65">{item.answer}</p>
              </details>
            ))}
          </div>
        </section>

        {/* Related landings — topical cluster + crawl paths */}
        {content.related.length > 0 && (
          <section className="border-t border-white/[0.06] py-12">
            <h2 className="text-xl font-bold tracking-tight">Смотрите также</h2>
            <div className="mt-5 flex flex-wrap gap-3">
              {content.related.map((rel) => (
                <Link
                  key={rel.href}
                  href={rel.href}
                  className="inline-flex items-center gap-2 rounded-lg border border-white/[0.08] bg-white/[0.02] px-4 py-2.5 text-sm text-white/70 transition-all hover:border-white/20 hover:text-white"
                >
                  {rel.title}
                  <ArrowRight className="h-3.5 w-3.5" />
                </Link>
              ))}
            </div>
          </section>
        )}
      </main>

      {/* Final CTA */}
      <section className="border-t border-white/[0.06] px-5 py-16">
        <div className="mx-auto max-w-3xl text-center">
          <h2 className="text-2xl font-bold tracking-tight sm:text-3xl">
            {content.finalCtaHeading}
          </h2>
          <p className="mx-auto mt-4 max-w-xl text-[15px] leading-relaxed text-white/65">
            {content.finalCtaSub}
          </p>
          <div className="mt-8 flex flex-col items-center justify-center gap-3 sm:flex-row">
            <CtaLink cta={content.finalCtaPrimary} variant="primary" />
            {content.finalCtaSecondary && (
              <CtaLink cta={content.finalCtaSecondary} variant="secondary" />
            )}
          </div>
        </div>
      </section>

      {/* Footer */}
      <footer className="border-t border-white/[0.06] px-5 py-10">
        <nav className="mx-auto flex max-w-4xl flex-wrap items-center justify-center gap-x-6 gap-y-3 text-sm text-white/50">
          <Link href={routes.home} className="transition-colors hover:text-white/80">
            Платформа
          </Link>
          <Link href={routes.pricing} className="transition-colors hover:text-white/80">
            Тарифы
          </Link>
        </nav>
        <p className="mt-6 text-center text-xs text-white/30">© SachkovLearn</p>
      </footer>
    </div>
  );
}
