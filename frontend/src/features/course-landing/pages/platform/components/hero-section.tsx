"use client";

import { ArrowRight } from "lucide-react";
import { DataGridHero } from "@/shared/ui/kit/data-grid-hero";
import { consultationLink } from "../config";
import { useReducedMotion } from "../hooks/use-reduced-motion";

// ---------------------------------------------------------------------------
// AnimatedTerminal — sequential dev pipeline (one-shot CSS fade-in)
// ---------------------------------------------------------------------------

function AnimatedTerminal() {
  return (
    <div className="rounded-2xl border border-white/[0.06] bg-[#0D0D0F] p-1 shadow-2xl shadow-black/50">
      {/* Title bar */}
      <div className="flex items-center gap-2 rounded-t-xl border-b border-white/[0.04] px-4 py-2.5">
        <div className="h-2.5 w-2.5 rounded-full bg-[#ff5f57]" />
        <div className="h-2.5 w-2.5 rounded-full bg-[#febc2e]" />
        <div className="h-2.5 w-2.5 rounded-full bg-[#28c840]" />
        <span className="ml-2 font-mono text-[11px] text-white/25">~/sachkov-learn</span>
      </div>

      {/* Body */}
      <div className="p-3 font-mono text-[13px] leading-relaxed sm:p-5 md:p-6 md:text-sm">
        <style>{`
          .htl{opacity:0;animation:htlIn .4s ease forwards}
          @keyframes htlIn{to{opacity:1}}
          @keyframes htlBlink{0%,100%{opacity:1}50%{opacity:0}}
          @media(prefers-reduced-motion:reduce){.htl{opacity:1!important;animation:none!important}}
        `}</style>

        {/* $ ./run-pipeline.sh */}
        <div className="htl" style={{ animationDelay: "0.3s" }}>
          <p className="text-white/40">
            <span className="text-[#6BADA5]">$</span> ./run-pipeline.sh
          </p>
        </div>

        <div className="htl" style={{ animationDelay: "0.6s" }}>
          <div className="my-2 border-t border-dashed border-white/[0.06]" />
        </div>

        {/* [01/06] Проектирование */}
        <div className="htl" style={{ animationDelay: "0.9s" }}>
          <div className="flex items-baseline gap-2">
            <span className="shrink-0 text-white/30">[01/06]</span>
            <span className="text-white/60">Проектирование...</span>
          </div>
        </div>
        <div className="htl" style={{ animationDelay: "1.1s" }}>
          <p className="pl-3">
            <span className="text-[#28c840]">✓</span>{" "}
            <span className="text-white/25">DDD • Clean Architecture</span>
          </p>
        </div>

        {/* [02/06] Реализация фичи */}
        <div className="htl mt-1" style={{ animationDelay: "1.5s" }}>
          <div className="flex items-baseline gap-2">
            <span className="shrink-0 text-white/30">[02/06]</span>
            <span className="text-white/60">Реализация фичи...</span>
          </div>
        </div>
        <div className="htl" style={{ animationDelay: "1.7s" }}>
          <p className="pl-3 text-white/40">
            <span className="text-[#6BADA5]">$</span> git switch -c feature/api-v2
          </p>
        </div>
        <div className="htl" style={{ animationDelay: "1.9s" }}>
          <p className="pl-3">
            <span className="text-[#28c840]">✓</span>{" "}
            <span className="text-white/25">Backend + Frontend готовы</span>
          </p>
        </div>

        {/* [03/06] Ревью и правки */}
        <div className="htl mt-1" style={{ animationDelay: "2.3s" }}>
          <div className="flex items-baseline gap-2">
            <span className="shrink-0 text-white/30">[03/06]</span>
            <span className="text-white/60">Ревью и правки...</span>
          </div>
        </div>
        <div className="htl" style={{ animationDelay: "2.5s" }}>
          <p className="pl-3 text-white/40">
            <span className="text-[#6BADA5]">$</span> git push feature/api-v2
          </p>
        </div>
        <div className="htl" style={{ animationDelay: "2.7s" }}>
          <p className="pl-3">
            <span className="text-[#28c840]">✓</span>{" "}
            <span className="text-white/25">PR reviewed &amp; merged</span>
          </p>
        </div>

        {/* [04/06] Тестирование */}
        <div className="htl mt-1" style={{ animationDelay: "3.1s" }}>
          <div className="flex items-baseline gap-2">
            <span className="shrink-0 text-white/30">[04/06]</span>
            <span className="text-white/60">Тестирование...</span>
          </div>
        </div>
        <div className="htl" style={{ animationDelay: "3.3s" }}>
          <p className="pl-3">
            <span className="text-[#28c840]">✓</span>{" "}
            <span className="text-white/25">48 passed, 0 failed</span>
          </p>
        </div>

        {/* [05/06] CI/CD Pipeline (gold) */}
        <div className="htl mt-1" style={{ animationDelay: "3.7s" }}>
          <div className="flex items-baseline gap-2">
            <span className="shrink-0 text-white/30">[05/06]</span>
            <span className="text-white/60">CI/CD Pipeline...</span>
            <span className="ml-auto shrink-0 rounded bg-[#C9A84C]/15 px-1.5 py-px text-[10px] text-[#C9A84C]">
              new
            </span>
          </div>
        </div>
        <div className="htl" style={{ animationDelay: "3.9s" }}>
          <p className="pl-3">
            <span className="text-[#28c840]">✓</span>{" "}
            <span className="text-white/25">Build → Test → Deploy</span>
          </p>
        </div>

        {/* [06/06] Продакшен (gold) */}
        <div className="htl mt-1" style={{ animationDelay: "4.3s" }}>
          <div className="flex items-baseline gap-2">
            <span className="shrink-0 text-white/30">[06/06]</span>
            <span className="text-white/60">Продакшен...</span>
            <span className="ml-auto shrink-0 rounded bg-[#C9A84C]/15 px-1.5 py-px text-[10px] text-[#C9A84C]">
              new
            </span>
          </div>
        </div>
        <div className="htl" style={{ animationDelay: "4.5s" }}>
          <p className="pl-3">
            <span className="text-[#28c840]">✓</span>{" "}
            <span className="text-white/25">5 сервисов • Мониторинг ОК</span>
          </p>
        </div>

        {/* Pipeline passed banner */}
        <div className="htl" style={{ animationDelay: "5.0s" }}>
          <div className="my-3">
            <div className="h-px w-full bg-white/[0.08]" />
            <p className="py-1.5 text-[#28c840]">{"  "}Pipeline passed ✓</p>
            <div className="h-px w-full bg-white/[0.08]" />
          </div>
        </div>

        {/* Telegram CTA + cursor */}
        <div className="htl" style={{ animationDelay: "5.5s" }}>
          <div className="flex items-center gap-2">
            <span className="text-white/30">$</span>
            <a
              href={consultationLink}
              target="_blank"
              rel="noopener noreferrer"
              className="group flex items-center gap-1.5 text-[#6BADA5] transition-colors hover:text-[#5CEAC9]"
            >
              <svg className="h-3 w-3" viewBox="0 0 24 24" fill="currentColor">
                <path d="M11.944 0A12 12 0 0 0 0 12a12 12 0 0 0 12 12 12 12 0 0 0 12-12A12 12 0 0 0 12 0a12 12 0 0 0-.056 0zm4.962 7.224c.1-.002.321.023.465.14a.506.506 0 0 1 .171.325c.016.093.036.306.02.472-.18 1.898-.962 6.502-1.36 8.627-.168.9-.499 1.201-.82 1.23-.696.065-1.225-.46-1.9-.902-1.056-.693-1.653-1.124-2.678-1.8-1.185-.78-.417-1.21.258-1.91.177-.184 3.247-2.977 3.307-3.23.007-.032.014-.15-.056-.212s-.174-.041-.249-.024c-.106.024-1.793 1.14-5.061 3.345-.48.33-.913.49-1.302.48-.428-.008-1.252-.241-1.865-.44-.752-.245-1.349-.374-1.297-.789.027-.216.325-.437.893-.663 3.498-1.524 5.83-2.529 6.998-3.014 3.332-1.386 4.025-1.627 4.476-1.635z" />
              </svg>
              <span className="underline decoration-[#6BADA5]/30 underline-offset-2 group-hover:decoration-[#5CEAC9]/50">
                получить консультацию
              </span>
            </a>
            <span
              className="inline-block h-3.5 w-1.5 bg-[#6BADA5]"
              style={{
                opacity: 0,
                animation: "htlIn 0.3s 6s ease forwards, htlBlink 1s 6.3s step-end infinite",
              }}
            />
          </div>
        </div>
      </div>
    </div>
  );
}

// ---------------------------------------------------------------------------
// HeroSection — entrance animations driven by CSS keyframes (issue #133).
// Pure-CSS path is bullet-proof on mobile Firefox/Waterfox where the previous
// framer-motion `initial="hidden"` + `useScroll`/`useTransform` combination
// could leave hero content stuck at opacity:0. Reduced-motion respected via
// @media query.
// ---------------------------------------------------------------------------

export function HeroSection() {
  const reduced = useReducedMotion();

  return (
    <section>
      <DataGridHero
        rows={16}
        cols={28}
        spacing={3}
        duration={4}
        color="#6BADA5"
        animationType="pulse"
        pulseEffect={!reduced}
        mouseGlow={!reduced}
        opacityMin={0.04}
        opacityMax={0.25}
        background="#0A0A0B"
        className="pt-36 pb-12 sm:pt-40 sm:pb-16 md:pt-42 md:pb-16 lg:pt-50 lg:pb-20"
      >
        <style>{`
          .hero-in{transform:translateY(20px);animation:heroIn .6s cubic-bezier(.25,.46,.45,.94) forwards}
          .hero-terminal-in{opacity:0;transform:translateY(30px) scale(.97);animation:heroTerminalIn .8s .3s cubic-bezier(.25,.46,.45,.94) forwards}
          @keyframes heroIn{to{transform:translateY(0)}}
          @keyframes heroTerminalIn{to{opacity:1;transform:translateY(0) scale(1)}}
          @media(prefers-reduced-motion:reduce){
            .hero-in,.hero-terminal-in{opacity:1!important;transform:none!important;animation:none!important}
          }
        `}</style>

        <div className="mx-auto max-w-7xl px-6">
          <div className="flex flex-col items-center gap-10 lg:flex-row lg:items-center lg:gap-12 xl:gap-16">
            {/* LEFT: Content — min-w-0 + max-width prevents long heading from squashing terminal */}
            <div className="min-w-0 flex-1 text-center lg:max-w-[34rem] lg:text-left xl:max-w-[40rem]">
              <h1 className="hero-in text-[2rem] font-bold leading-[1.1] tracking-tight sm:text-5xl md:text-6xl lg:text-[3.5rem] xl:text-[4rem]">
                Освой{" "}
                <span className="bg-gradient-to-r from-[#6BADA5] to-[#5CEAC9] bg-clip-text text-transparent">
                  .NET Fullstack
                </span>{" "}
                разработку
              </h1>

              <p
                className="hero-in mx-auto mt-4 max-w-md text-sm leading-relaxed text-white/50 sm:mt-6 sm:max-w-lg sm:text-base lg:mx-0 lg:text-lg"
                style={{ animationDelay: "0.12s" }}
              >
                От&nbsp;C# и&nbsp;ASP.NET Core API до&nbsp;React, микросервисов, деплоя
                и&nbsp;AI-инструментов. Полный доступ открывает программу .NET Fullstack: курсы,
                задания, AI-ревью PR и&nbsp;закрытый чат на&nbsp;реальных проектах.
              </p>

              <div
                className="hero-in mt-6 flex flex-col items-center gap-3 sm:mt-8 sm:flex-row sm:justify-center lg:justify-start"
                style={{ animationDelay: "0.24s" }}
              >
                <a
                  href="#price"
                  data-growth-cta="hero_full_access"
                  data-growth-placement="hero"
                  className="inline-flex items-center justify-center gap-2 rounded-lg bg-[#6BADA5] px-7 py-3 text-sm font-medium text-[#0A0A0B] transition-all hover:bg-[#5CEAC9] hover:shadow-[0_0_30px_rgba(107,173,165,0.3)]"
                >
                  Получить полный доступ <ArrowRight className="h-4 w-4" />
                </a>
                <a
                  href="#program"
                  data-growth-cta="hero_program"
                  data-growth-placement="hero"
                  className="inline-flex items-center justify-center gap-2 rounded-lg border border-white/[0.08] px-7 py-3 text-sm font-medium text-white/60 transition-all hover:border-white/20 hover:text-white"
                >
                  Программа обучения
                </a>
              </div>
            </div>

            {/* RIGHT: Terminal */}
            <div className="hero-terminal-in w-full max-w-lg sm:max-w-xl lg:w-[48%] lg:max-w-none lg:flex-shrink-0 xl:w-[52%]">
              <AnimatedTerminal />
            </div>
          </div>
        </div>
      </DataGridHero>
    </section>
  );
}
