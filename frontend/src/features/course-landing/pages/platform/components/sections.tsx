"use client";

import { useState } from "react";
import { BusinessDetailsView, type BusinessDetails } from "@/shared/business-details";
import Link from "next/link";
import {
  ChevronDown,
  Check,
  ArrowRight,
  Code,
  BookOpen,
  Layers,
  Terminal,
  Bot,
  Shield,
  Zap,
  Cpu,
  Globe,
  Server,
  Rocket,
} from "lucide-react";
import { routes } from "@/shared/config/routes";
import { useCookieConsent } from "@/shared/lib/use-cookie-consent";
import {
  COURSE_SLUG,
  trackFoundation,
  trackEngineering,
  consultationLink,
  author,
  faq,
} from "../config";
import { ScrollReveal, StaggerContainer, StaggerItem } from "./scroll-reveal";

// Track 2 topic icons (gold accent)
const GOLD = "#C9A84C";

const track2Icons: Record<string, React.ReactNode> = {
  "AI в разработке": <Bot className="h-4 w-4" />,
  "Архитектура и паттерны": <Layers className="h-4 w-4" />,
  "Микросервисы в продакшене": <Server className="h-4 w-4" />,
  "Продакшен-инфраструктура": <Rocket className="h-4 w-4" />,
  "Инфраструктура и DevOps": <Terminal className="h-4 w-4" />,
  "Проектирование систем": <Cpu className="h-4 w-4" />,
  "Тестирование и качество": <Check className="h-4 w-4" />,
  Performance: <Zap className="h-4 w-4" />,
  Безопасность: <Shield className="h-4 w-4" />,
  "Soft skills и карьера": <Globe className="h-4 w-4" />,
};

// ===========================================================================
// PROGRAM
// ===========================================================================

export function ProgramSection() {
  return (
    <section id="program" className="relative py-10 md:py-16 lg:py-20">
      <div className="mx-auto max-w-7xl px-6">
        <ScrollReveal>
          <h2 className="text-center text-3xl font-bold tracking-tight sm:text-4xl md:text-5xl">
            Программа обучения
          </h2>
          <p className="mx-auto mt-4 max-w-2xl text-center text-base text-white/50 md:text-lg">
            Два уровня одного пути — оба на&nbsp;.NET. Фундамент выводит на&nbsp;junior, Software
            Engineer углубляет тот&nbsp;же стек до&nbsp;production/senior. Программа постоянно
            дополняется вместе с&nbsp;индустрией.
          </p>
        </ScrollReveal>

        <StaggerContainer className="mt-12 grid gap-6 lg:mt-16 lg:grid-cols-2 lg:gap-8">
          {/* Track 1 — Foundation (grouped) */}
          <StaggerItem>
            <div className="relative flex h-full flex-col rounded-2xl border border-white/[0.06] bg-[#141416] p-6 transition-all duration-300 hover:-translate-y-1 hover:border-white/[0.12] sm:p-8">
              <div className="flex items-center gap-3">
                <span className="text-xs font-semibold uppercase tracking-wider text-[#6BADA5]">
                  {trackFoundation.label}
                </span>
              </div>
              <h3 className="mt-3 text-2xl font-bold">{trackFoundation.title}</h3>
              <p className="mt-1 text-sm text-white/40 lg:text-[15px]">
                {trackFoundation.subtitle} &middot; {trackFoundation.stats}
              </p>

              <div className="mt-6 flex-1 space-y-6">
                {trackFoundation.groups.map((group) => (
                  <div key={group.title}>
                    <div className="flex items-center justify-between">
                      <span className="text-[11px] font-semibold uppercase tracking-wider text-white/35 sm:text-xs lg:text-sm">
                        {group.title}
                      </span>
                      <span className="text-[11px] tabular-nums text-white/30 sm:text-xs lg:text-sm">
                        {group.lessonCount} уроков
                      </span>
                    </div>
                    <ul className="mt-2.5 space-y-2">
                      {group.topics.map((topic) => (
                        <li
                          key={topic}
                          className="flex items-center gap-2.5 text-[15px] text-white/55 lg:text-base"
                        >
                          <span className="h-1 w-1 flex-shrink-0 rounded-full bg-[#6BADA5]/50" />
                          {topic}
                        </li>
                      ))}
                    </ul>
                  </div>
                ))}
              </div>

              <div className="mt-6 border-t border-white/[0.04] pt-4">
                <span className="flex items-center gap-2 text-sm font-medium text-[#6BADA5]">
                  <BookOpen className="h-4 w-4" /> {trackFoundation.finalProject}
                </span>
              </div>
            </div>
          </StaggerItem>

          {/* Track 2 — Engineering (gold premium) */}
          <StaggerItem>
            <div
              className="group relative flex h-full flex-col overflow-hidden rounded-2xl border bg-[#141416] p-6 transition-all duration-300 hover:-translate-y-1 sm:p-8"
              style={{
                borderColor: `${GOLD}33`,
                boxShadow: `0 0 32px ${GOLD}0a`,
              }}
            >
              {/* Animated grid pattern в фоне (медленно дрейфует) */}
              <style>{`
                @keyframes track2GridDrift {
                  0%   { background-position: 0 0; }
                  100% { background-position: 40px 40px; }
                }
                @media (prefers-reduced-motion: reduce) {
                  .track2-grid { animation: none !important; }
                }
              `}</style>
              <div
                className="track2-grid pointer-events-none absolute inset-0 rounded-2xl opacity-[0.04]"
                style={{
                  backgroundImage: `linear-gradient(${GOLD} 1px, transparent 1px), linear-gradient(90deg, ${GOLD} 1px, transparent 1px)`,
                  backgroundSize: "40px 40px",
                  animation: "track2GridDrift 18s linear infinite",
                  willChange: "background-position",
                }}
                aria-hidden="true"
              />

              {/* Subtle gold gradient overlay */}
              <div
                className="pointer-events-none absolute inset-0 rounded-2xl opacity-[0.05] transition-opacity duration-500 group-hover:opacity-[0.08]"
                style={{
                  background: `radial-gradient(ellipse at top right, ${GOLD}, transparent 70%)`,
                }}
              />

              <div className="relative flex items-center gap-3">
                <span
                  className="text-xs font-semibold uppercase tracking-wider"
                  style={{ color: GOLD }}
                >
                  {trackEngineering.label}
                </span>
                <span
                  className="rounded-full px-2.5 py-0.5 text-xs font-medium"
                  style={{
                    color: GOLD,
                    backgroundColor: `${GOLD}18`,
                    border: `1px solid ${GOLD}40`,
                  }}
                >
                  NEW
                </span>
              </div>
              <h3 className="relative mt-3 text-2xl font-bold">{trackEngineering.title}</h3>
              <p className="relative mt-1 text-sm text-white/40 lg:text-[15px]">
                {trackEngineering.subtitle} &middot; {trackEngineering.stats}
              </p>

              {/* Bridge — SE = тот же .NET, продолжение Фундамента (а не отдельное направление) */}
              <p className="relative mt-3 text-sm leading-relaxed text-white/55">
                {trackEngineering.bridge}
              </p>

              {/* Live status — prominent */}
              <div className="relative mt-4 flex items-center gap-2">
                <span className="relative flex h-2 w-2">
                  <span
                    className="absolute inline-flex h-full w-full animate-ping rounded-full opacity-60"
                    style={{ backgroundColor: GOLD }}
                  />
                  <span
                    className="relative inline-flex h-2 w-2 rounded-full"
                    style={{ backgroundColor: GOLD }}
                  />
                </span>
                <span className="text-sm font-medium" style={{ color: GOLD }}>
                  {trackEngineering.note}
                </span>
              </div>

              <p className="relative mt-6 text-xs font-medium uppercase tracking-wider text-white/35">
                {trackEngineering.topicsLabel}
              </p>
              <ul className="relative mt-3 flex-1 space-y-3.5">
                {trackEngineering.topics.map((topic) => (
                  <li
                    key={topic.name}
                    className="flex items-center gap-3 text-[15px] text-white/60 lg:text-base"
                  >
                    <span
                      className="flex h-5 w-5 flex-shrink-0 items-center justify-center"
                      style={{ color: GOLD }}
                    >
                      {track2Icons[topic.name] ?? <Code className="h-4 w-4" />}
                    </span>
                    {topic.name}
                  </li>
                ))}
              </ul>

              <div className="relative mt-6" />
            </div>
          </StaggerItem>
        </StaggerContainer>

        {/* Full program CTA */}
        <ScrollReveal>
          <div className="mt-10 flex justify-center lg:mt-14">
            <a
              href={routes.courses}
              data-growth-cta="program_courses"
              data-growth-placement="course"
              className="group inline-flex items-center gap-2 rounded-xl border border-white/[0.08] bg-white/[0.02] px-6 py-3.5 text-sm font-medium text-white/70 transition-all hover:border-white/20 hover:bg-white/[0.04] hover:text-white"
            >
              Смотреть курсы .NET Fullstack
              <ArrowRight className="h-4 w-4 transition-transform group-hover:translate-x-1" />
            </a>
          </div>
        </ScrollReveal>
      </div>
    </section>
  );
}

// TestimonialsSection moved to testimonials-section.tsx (server component)

// ===========================================================================
// AUTHOR
// ===========================================================================

export function AuthorSection() {
  return (
    <section id="author" className="relative py-10 md:py-16 lg:py-20">
      <div className="mx-auto max-w-7xl px-6">
        <ScrollReveal>
          <h2 className="text-center text-3xl font-bold tracking-tight sm:text-4xl md:text-5xl">
            Автор платформы
          </h2>
        </ScrollReveal>

        <ScrollReveal delay={0.15}>
          <div className="mx-auto mt-12 flex max-w-4xl flex-col items-center gap-8 lg:mt-16 lg:flex-row lg:items-start lg:gap-14">
            {/* Photo + socials */}
            <div className="relative flex flex-shrink-0 flex-col items-center gap-4">
              <style>{`
                @keyframes authorFloat {
                  0%, 100% { transform: translateY(0); }
                  50%      { transform: translateY(-6px); }
                }
                @media (prefers-reduced-motion: reduce) {
                  .author-photo-float { animation: none !important; }
                }
              `}</style>
              {/* Wrapper — float'ит фото И glow как единое целое.
                  Так glow не «отрывается» при transform-stacking-context'е. */}
              <div
                className="author-photo-float relative"
                style={{
                  animation: "authorFloat 5s ease-in-out infinite",
                  willChange: "transform",
                }}
              >
                {/* Glow за фотографией */}
                <div
                  aria-hidden="true"
                  className="pointer-events-none absolute -inset-6 rounded-[2rem] bg-[#6BADA5]/15 blur-3xl"
                />
                <div className="relative h-56 w-48 overflow-hidden rounded-2xl border border-white/[0.06] bg-gradient-to-b from-[#141416] to-[#0E0E11] lg:h-72 lg:w-60">
                  {/* eslint-disable-next-line @next/next/no-img-element */}
                  <img
                    src={author.photo}
                    alt=""
                    className="h-full w-full object-cover object-[center_20%]"
                  />
                </div>
              </div>

              {/* Social links under photo */}
              <div className="flex items-center gap-3">
                <SocialLink href={author.socials.youtube} label="YouTube">
                  <path d="M23.5 6.19a3.02 3.02 0 0 0-2.12-2.14C19.54 3.5 12 3.5 12 3.5s-7.54 0-9.38.55A3.02 3.02 0 0 0 .5 6.19 31.7 31.7 0 0 0 0 12a31.7 31.7 0 0 0 .5 5.81 3.02 3.02 0 0 0 2.12 2.14c1.84.55 9.38.55 9.38.55s7.54 0 9.38-.55a3.02 3.02 0 0 0 2.12-2.14A31.7 31.7 0 0 0 24 12a31.7 31.7 0 0 0-.5-5.81zM9.55 15.57V8.43L15.82 12l-6.27 3.57z" />
                </SocialLink>
                <SocialLink href={author.socials.telegram} label="Telegram-канал">
                  <path d="M11.94 24c6.627 0 12-5.373 12-12s-5.373-12-12-12-12 5.373-12 12 5.373 12 12 12zm-1.7-7.23l.2-3.01 5.55-5.02c.24-.22-.05-.33-.38-.13l-6.86 4.33-2.96-.92c-.64-.2-.65-.64.14-.95l11.57-4.46c.53-.24 1.03.13.83.95l-1.97 9.28c-.14.66-.54.82-1.1.51l-3.03-2.24-1.46 1.41c-.16.16-.3.3-.53.3z" />
                </SocialLink>
                <SocialLink href={author.socials.github} label="GitHub">
                  <path d="M12 .5C5.37.5 0 5.87 0 12.5c0 5.3 3.44 9.8 8.21 11.39.6.11.82-.26.82-.58v-2.03c-3.34.73-4.04-1.61-4.04-1.61-.55-1.39-1.34-1.76-1.34-1.76-1.09-.75.08-.73.08-.73 1.21.08 1.85 1.24 1.85 1.24 1.07 1.84 2.81 1.31 3.5 1 .11-.78.42-1.31.76-1.61-2.67-.3-5.47-1.33-5.47-5.93 0-1.31.47-2.38 1.24-3.22-.13-.3-.54-1.52.12-3.18 0 0 1.01-.32 3.3 1.23a11.5 11.5 0 0 1 6.02 0c2.3-1.55 3.3-1.23 3.3-1.23.66 1.66.25 2.88.12 3.18.77.84 1.24 1.91 1.24 3.22 0 4.61-2.81 5.63-5.48 5.92.43.37.81 1.1.81 2.22v3.29c0 .32.22.7.82.58A12.01 12.01 0 0 0 24 12.5C24 5.87 18.63.5 12 .5z" />
                </SocialLink>
              </div>
            </div>

            {/* Content */}
            <div className="space-y-5 text-center lg:text-left">
              <div>
                <h3 className="text-2xl font-bold lg:text-3xl">{author.name}</h3>
                <p className="mt-1.5 text-sm text-[#6BADA5]">{author.role}</p>
              </div>

              <p className="text-sm leading-relaxed text-white/50 md:text-base lg:text-[15px]">
                {author.bio}
              </p>

              <p className="text-sm font-medium text-white/70 lg:text-base">{author.cta}</p>

              <a
                href={consultationLink}
                target="_blank"
                rel="noopener noreferrer"
                className="inline-flex items-center gap-2 rounded-lg bg-[#6BADA5] px-5 py-2.5 text-sm font-medium text-[#0A0A0B] transition-all hover:bg-[#5CEAC9]"
              >
                <svg className="h-4 w-4" viewBox="0 0 24 24" fill="currentColor">
                  <path d="M11.944 0A12 12 0 0 0 0 12a12 12 0 0 0 12 12 12 12 0 0 0 12-12A12 12 0 0 0 12 0a12 12 0 0 0-.056 0zm4.962 7.224c.1-.002.321.023.465.14a.506.506 0 0 1 .171.325c.016.093.036.306.02.472-.18 1.898-.962 6.502-1.36 8.627-.168.9-.499 1.201-.82 1.23-.696.065-1.225-.46-1.9-.902-1.056-.693-1.653-1.124-2.678-1.8-1.185-.78-.417-1.21.258-1.91.177-.184 3.247-2.977 3.307-3.23.007-.032.014-.15-.056-.212s-.174-.041-.249-.024c-.106.024-1.793 1.14-5.061 3.345-.48.33-.913.49-1.302.48-.428-.008-1.252-.241-1.865-.44-.752-.245-1.349-.374-1.297-.789.027-.216.325-.437.893-.663 3.498-1.524 5.83-2.529 6.998-3.014 3.332-1.386 4.025-1.627 4.476-1.635z" />
                </svg>
                Получить консультацию
              </a>
            </div>
          </div>
        </ScrollReveal>
      </div>
    </section>
  );
}

function SocialLink({
  href,
  label,
  children,
}: {
  href: string;
  label: string;
  children: React.ReactNode;
}) {
  return (
    <a
      href={href}
      target="_blank"
      rel="noopener noreferrer"
      className="inline-flex text-white/30 transition-all duration-300 hover:-translate-y-1 hover:text-[#6BADA5]"
      aria-label={label}
    >
      <svg className="h-5 w-5" fill="currentColor" viewBox="0 0 24 24">
        {children}
      </svg>
    </a>
  );
}

// ===========================================================================
// FAQ
// ===========================================================================

export function FaqSection() {
  const [openFaq, setOpenFaq] = useState<number | null>(null);

  return (
    <section className="relative py-10 md:py-16 lg:py-20">
      <div className="mx-auto max-w-3xl px-6">
        <ScrollReveal>
          <h2 className="text-center text-3xl font-bold tracking-tight sm:text-4xl md:text-5xl">
            Частые вопросы
          </h2>
        </ScrollReveal>

        <div className="mt-16 divide-y divide-white/[0.06]">
          {faq.map((item, i) => (
            <ScrollReveal key={item.question} delay={i * 0.05}>
              <div>
                <button
                  onClick={() => setOpenFaq(openFaq === i ? null : i)}
                  className="flex w-full items-center justify-between py-6 text-left"
                >
                  <span className="pr-4 text-sm font-medium md:text-base">{item.question}</span>
                  <ChevronDown
                    className={`h-5 w-5 flex-shrink-0 text-white/40 transition-transform duration-200 ${
                      openFaq === i ? "rotate-180" : ""
                    }`}
                  />
                </button>
                <div
                  className={`grid transition-all duration-300 ease-in-out ${
                    openFaq === i ? "grid-rows-[1fr] opacity-100" : "grid-rows-[0fr] opacity-0"
                  }`}
                >
                  <div className="overflow-hidden">
                    <p className="pb-6 text-sm leading-relaxed text-white/50 md:text-base">
                      {item.answer}
                    </p>
                  </div>
                </div>
              </div>
            </ScrollReveal>
          ))}
        </div>
      </div>
    </section>
  );
}

// ===========================================================================
// FINAL CTA
// ===========================================================================

export function FinalCtaSection() {
  return (
    <section className="relative py-10 md:py-16 lg:py-20">
      <div className="mx-auto max-w-3xl px-6 text-center">
        <ScrollReveal variant="scaleIn">
          <h2 className="bg-gradient-to-r from-[#6BADA5] to-[#5CEAC9] bg-clip-text text-3xl font-bold tracking-tight text-transparent sm:text-4xl md:text-5xl">
            Начни .NET Fullstack путь
          </h2>
          <p className="mt-4 text-white/50 md:text-lg">
            Начни бесплатно с демо-уроков. Полный доступ .NET Fullstack открывает оба уровня,
            задания, AI-ревью PR и закрытый чат.
          </p>
          <div className="mt-8 flex flex-col items-center gap-3 sm:flex-row sm:justify-center">
            <a
              href={routes.courses}
              data-growth-cta="final_courses"
              data-growth-placement="footer"
              className="group relative inline-flex items-center gap-2 rounded-lg bg-[#6BADA5] px-8 py-3.5 text-sm font-medium text-[#0A0A0B] transition-all hover:bg-[#5CEAC9] hover:shadow-[0_0_40px_rgba(107,173,165,0.45)]"
            >
              {/* Pulsing glow ring */}
              <span
                className="pointer-events-none absolute inset-0 -z-10 rounded-lg"
                style={{
                  background:
                    "radial-gradient(ellipse at center, rgba(107,173,165,0.4), transparent 70%)",
                  animation: "fctaPulse 2.4s ease-in-out infinite",
                  filter: "blur(8px)",
                }}
                aria-hidden="true"
              />
              <style>{`
                @keyframes fctaPulse {
                  0%, 100% { opacity: 0.6; transform: scale(1); }
                  50%      { opacity: 1;   transform: scale(1.08); }
                }
              `}</style>
              Открыть платформу{" "}
              <ArrowRight className="h-4 w-4 transition-transform group-hover:translate-x-0.5" />
            </a>
            <a
              href={consultationLink}
              data-growth-cta="final_consultation"
              data-growth-placement="footer"
              target="_blank"
              rel="noopener noreferrer"
              className="inline-flex items-center gap-2 rounded-lg border border-white/[0.08] px-8 py-3.5 text-sm font-medium text-white/60 transition-all hover:border-white/20 hover:text-white"
            >
              Получить консультацию
            </a>
          </div>
        </ScrollReveal>
      </div>
    </section>
  );
}

// ===========================================================================
// FOOTER
// ===========================================================================

const LEGAL_LINKS = [
  { href: "/legal/offer", label: "Оферта" },
  { href: "/legal/privacy", label: "Политика ПДн" },
  { href: "/legal/consent-pd", label: "Согласие ПДн" },
  { href: "/legal/cookies", label: "Cookies" },
  { href: "/legal/consent-marketing", label: "Согласие на рассылку" },
];

export function FooterSection({
  businessDetails = null,
}: {
  businessDetails?: BusinessDetails | null;
}) {
  const courseUrl = routes.courseOverview(COURSE_SLUG);
  const { reset } = useCookieConsent();

  return (
    <footer className="relative border-t border-white/[0.06] py-12 md:py-16">
      <div className="mx-auto max-w-7xl px-6">
        <div className="grid gap-10 sm:grid-cols-2 md:grid-cols-4">
          <div className="space-y-4 sm:col-span-2 md:col-span-1">
            <span className="text-lg font-bold tracking-tight">
              Sachkov<span className="text-[#6BADA5]">Learn</span>
            </span>
            <p className="text-sm leading-relaxed text-white/40">
              Платформа полного цикла для .NET-инженеров: обучение от основ до senior, практика и
              сообщество.
            </p>
          </div>

          <div className="space-y-3">
            <p className="text-xs font-semibold uppercase tracking-wider text-white/30">
              Навигация
            </p>
            <nav className="flex flex-col gap-2">
              {[
                { label: "Программа", href: "#program" },
                { label: "Обучение", href: "#learning" },
                { label: "Отзывы", href: "#testimonials" },
                { label: "Автор", href: "#author" },
                { label: "Цена", href: "#price" },
              ].map((link) => (
                <a
                  key={link.href}
                  href={link.href}
                  className="text-sm text-white/40 transition-colors hover:text-white/60"
                >
                  {link.label}
                </a>
              ))}
            </nav>
          </div>

          <div className="space-y-3">
            <p className="text-xs font-semibold uppercase tracking-wider text-white/30">Контакты</p>
            <div className="flex flex-col gap-2">
              <a
                href={consultationLink}
                target="_blank"
                rel="noopener noreferrer"
                className="text-sm text-white/40 transition-colors hover:text-white/60"
              >
                Консультация
              </a>
              <a
                href={author.socials.telegram}
                target="_blank"
                rel="noopener noreferrer"
                className="text-sm text-white/40 transition-colors hover:text-white/60"
              >
                Telegram-канал
              </a>
              <a
                href={author.socials.youtube}
                target="_blank"
                rel="noopener noreferrer"
                className="text-sm text-white/40 transition-colors hover:text-white/60"
              >
                YouTube
              </a>
              <a
                href={author.socials.github}
                target="_blank"
                rel="noopener noreferrer"
                className="text-sm text-white/40 transition-colors hover:text-white/60"
              >
                GitHub
              </a>
            </div>
          </div>

          <div className="space-y-3">
            <p className="text-xs font-semibold uppercase tracking-wider text-white/30">Курс</p>
            <div className="flex flex-col gap-2">
              <a
                href={courseUrl}
                className="text-sm text-white/40 transition-colors hover:text-white/60"
              >
                Перейти к курсу
              </a>
              <a
                href="#faq"
                className="text-sm text-white/40 transition-colors hover:text-white/60"
              >
                Частые вопросы
              </a>
            </div>
          </div>
        </div>

        <div className="mt-10 grid gap-8 border-t border-white/[0.04] pt-8 md:grid-cols-[1fr_auto]">
          <address className="not-italic space-y-1 text-xs text-white/40">
            {businessDetails ? <BusinessDetailsView details={businessDetails} compact /> : null}
            <div className="pt-1 text-white/30">
              Услуги не являются образовательной деятельностью по 273-ФЗ.
            </div>
          </address>

          <div className="space-y-3 md:text-right">
            <p className="text-xs font-semibold uppercase tracking-wider text-white/30">
              Документы
            </p>
            <nav className="flex flex-wrap gap-x-4 gap-y-2 md:justify-end">
              {LEGAL_LINKS.map((link) => (
                <Link
                  key={link.href}
                  href={link.href}
                  className="inline-flex min-h-11 items-center text-xs text-white/60 transition-colors hover:text-white/80"
                >
                  {link.label}
                </Link>
              ))}
              <button
                type="button"
                onClick={reset}
                className="inline-flex min-h-11 items-center text-xs text-white/60 underline transition-colors hover:text-white/80"
              >
                Настройки cookies
              </button>
            </nav>
          </div>
        </div>

        <div className="mt-8 border-t border-white/[0.04] pt-6">
          <p className="text-center text-xs text-white/25">
            &copy; {new Date().getFullYear()} {businessDetails?.copyrightName ?? "SachkovLearn"} Все
            права защищены.
          </p>
        </div>
      </div>
    </footer>
  );
}
