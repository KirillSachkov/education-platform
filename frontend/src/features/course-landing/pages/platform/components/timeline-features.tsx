"use client";

import { useRef } from "react";
import { motion, useScroll, useTransform, useInView } from "framer-motion";
import { ArrowRight, BookOpen, Code, Layers, Rocket, Briefcase, FileText } from "lucide-react";
import { targetAudience } from "../config";
import { useReducedMotion } from "../hooks/use-reduced-motion";
import { ScrollReveal } from "./scroll-reveal";

// ---------------------------------------------------------------------------
// Sub-features per audience segment
// ---------------------------------------------------------------------------

const subFeatures: Record<string, Array<{ icon: React.ReactNode; title: string; desc: string }>> = {
  "01": [
    {
      icon: <BookOpen className="h-4 w-4" />,
      title: "Структурированный путь",
      desc: "От архитектуры и DDD до деплоя в продакшен",
    },
    {
      icon: <Code className="h-4 w-4" />,
      title: "Практика с первого дня",
      desc: "Задания на реальных бизнес-кейсах, не учебных",
    },
    {
      icon: <Rocket className="h-4 w-4" />,
      title: "Сообщество и поддержка",
      desc: "Закрытый чат, разборы, автор всегда на связи",
    },
  ],
  "02": [
    {
      icon: <Layers className="h-4 w-4" />,
      title: "Углублённая архитектура",
      desc: "CQRS, продвинутый DDD, паттерны",
    },
    {
      icon: <Rocket className="h-4 w-4" />,
      title: "Микросервисы",
      desc: "Разбиение монолита, контракты сервисов, обмен данными",
    },
    {
      icon: <Code className="h-4 w-4" />,
      title: "Performance и concurrency",
      desc: "Блокировки, оптимизация запросов, profiling",
    },
  ],
  "03": [
    {
      icon: <Layers className="h-4 w-4" />,
      title: "System Design",
      desc: "Проектирование и архитектурные решения",
    },
    {
      icon: <Code className="h-4 w-4" />,
      title: "AI в разработке",
      desc: "AI-ассистенты в повседневных задачах",
    },
    {
      icon: <Rocket className="h-4 w-4" />,
      title: "Продакшен и Soft Skills",
      desc: "Инфраструктура, мониторинг, коммуникация",
    },
  ],
  "04": [
    {
      icon: <FileText className="h-4 w-4" />,
      title: "Резюме и портфолио",
      desc: "Упаковка проектов, ATS-friendly CV, презентация опыта",
    },
    {
      icon: <Briefcase className="h-4 w-4" />,
      title: "Поиск работы",
      desc: "Где искать вакансии и как откликаться",
    },
  ],
};

const categoryBadges: Record<string, string> = {
  "01": "Этап 1 — Старт",
  "02": "Этап 2 — Рост",
  "03": "Этап 3 — Инженер",
  "04": "Этап 4 — Поиск работы",
};

// ---------------------------------------------------------------------------
// Timeline dot — lights up when in view
// ---------------------------------------------------------------------------

const GOLD_IDS = new Set(["03", "04"]);
const isGoldId = (id: string) => GOLD_IDS.has(id);

function TimelineDot({ index }: { index: number }) {
  const ref = useRef<HTMLDivElement>(null);
  const inView = useInView(ref, { once: true, margin: "-40%" });
  const reduced = useReducedMotion();
  const isGold = index >= 2;
  const activeColor = isGold ? "#C9A84C" : "#6BADA5";
  const glowColor = isGold ? "rgba(201,168,76,0.5)" : "rgba(107,173,165,0.5)";

  return (
    <motion.div
      ref={ref}
      className="absolute -left-[15px] top-6 z-10 h-2.5 w-2.5 rounded-full border-2 border-[#0E0E11] sm:-left-[16px] sm:top-8 sm:h-3 sm:w-3 lg:-left-[7px] lg:h-3.5 lg:w-3.5"
      initial={
        reduced
          ? { opacity: 1, backgroundColor: activeColor, boxShadow: `0 0 8px ${glowColor}` }
          : { opacity: 0 }
      }
      animate={
        inView
          ? { opacity: 1, backgroundColor: activeColor, boxShadow: `0 0 12px ${glowColor}` }
          : undefined
      }
      transition={{ duration: 0.4, delay: index * 0.1 }}
    />
  );
}

// ---------------------------------------------------------------------------
// TimelineFeatures — Railway-style vertical timeline
// ---------------------------------------------------------------------------

export function TimelineFeatures() {
  const reduced = useReducedMotion();
  const containerRef = useRef<HTMLDivElement>(null);
  const { scrollYProgress } = useScroll({
    target: containerRef,
    offset: ["start 80%", "end 50%"],
  });
  const lineHeight = useTransform(scrollYProgress, [0, 1], ["0%", "100%"]);

  return (
    <section className="relative py-10 md:py-16 lg:py-20">
      <div className="mx-auto max-w-7xl px-6">
        <ScrollReveal>
          <h2 className="text-center text-3xl font-bold tracking-tight sm:text-4xl md:text-5xl">
            Карта пути по платформе
          </h2>
          <p className="mx-auto mt-4 max-w-lg text-center text-base text-white/50 md:text-lg">
            Четыре этапа одного пути — от основ C# до senior-инженера. Стек один, глубина растёт.
          </p>
        </ScrollReveal>

        {/* Timeline container */}
        <div ref={containerRef} className="relative mt-10 sm:mt-12 lg:mt-20">
          {/* Vertical line — teal→gold gradient, scroll-animated */}
          <div
            className="absolute left-0 top-0 h-full w-px sm:left-0"
            style={{ backgroundColor: "rgba(255,255,255,0.04)" }}
          >
            <motion.div
              className="w-full origin-top"
              style={{
                height: reduced ? "100%" : lineHeight,
                background:
                  "linear-gradient(to bottom, transparent 0%, #6BADA580 4%, #6BADA5 12%, #6BADA5 55%, #C9A84C 75%, #C9A84C40 100%)",
              }}
            />
          </div>

          {/* Feature blocks */}
          <div className="space-y-14 pl-5 sm:space-y-16 sm:pl-6 lg:space-y-24 lg:pl-12">
            {targetAudience.map((item, i) => (
              <div key={item.id} className="relative">
                {/* Timeline dot */}
                <TimelineDot index={i} />

                <ScrollReveal delay={i * 0.1}>
                  <div className="flex flex-col gap-8 lg:flex-row lg:items-start lg:gap-16">
                    {/* LEFT: Text content */}
                    <div className="flex-1 space-y-5">
                      {/* Category badge */}
                      <div className="flex flex-wrap items-center gap-2">
                        <span
                          className="inline-flex rounded-full px-3 py-1 text-xs font-medium"
                          style={
                            isGoldId(item.id)
                              ? {
                                  color: "#C9A84C",
                                  backgroundColor: "#C9A84C0d",
                                  border: "1px solid #C9A84C33",
                                }
                              : {
                                  color: "#6BADA5",
                                  backgroundColor: "rgba(107,173,165,0.05)",
                                  border: "1px solid rgba(107,173,165,0.2)",
                                }
                          }
                        >
                          {categoryBadges[item.id]}
                        </span>
                        {item.id === "04" && (
                          <span
                            className="inline-flex rounded-full px-3 py-1 text-xs font-medium"
                            style={{
                              color: "#C9A84C",
                              backgroundColor: "#C9A84C18",
                              border: "1px solid #C9A84C40",
                            }}
                          >
                            Новый этап платформы
                          </span>
                        )}
                      </div>

                      <h3 className="text-2xl font-bold sm:text-3xl">{item.title}</h3>
                      <p className="max-w-lg text-base leading-relaxed text-white/50 md:text-lg">
                        {item.description}
                      </p>

                      {/* Outcome */}
                      <div className="space-y-1.5">
                        <div
                          className="flex items-center gap-2 text-sm font-medium"
                          style={{ color: isGoldId(item.id) ? "#C9A84C" : "#6BADA5" }}
                        >
                          <ArrowRight className="h-4 w-4" />
                          {item.outcome}
                        </div>
                        {item.id === "04" && (
                          <p className="pl-6 text-xs text-white/30">
                            Трек активно дополняется — рынок меняется, материалы обновляются
                          </p>
                        )}
                      </div>

                      {/* Sub-features */}
                      <div className="mt-4 space-y-3 border-t border-white/[0.04] pt-5">
                        {subFeatures[item.id]?.map((f, j) => (
                          <div key={j} className="flex items-start gap-3">
                            <span
                              className="mt-0.5 flex-shrink-0"
                              style={{ color: isGoldId(item.id) ? "#C9A84C" : "#6BADA5" }}
                            >
                              {f.icon}
                            </span>
                            <div>
                              <p className="text-sm font-medium">{f.title}</p>
                              <p className="text-xs text-white/40">{f.desc}</p>
                            </div>
                          </div>
                        ))}
                      </div>
                    </div>

                    {/* RIGHT: Visual card with 3D-tilt on hover */}
                    <div
                      className="w-full lg:w-[45%] lg:flex-shrink-0"
                      style={{ perspective: 1200 }}
                    >
                      <motion.div
                        className="overflow-hidden rounded-2xl border bg-[#141416] p-6 transition-shadow duration-500"
                        style={
                          isGoldId(item.id)
                            ? {
                                borderColor: "#C9A84C33",
                                boxShadow: "0 0 24px #C9A84C0f",
                                willChange: "transform",
                              }
                            : { borderColor: "rgba(255,255,255,0.06)", willChange: "transform" }
                        }
                        whileHover={
                          reduced
                            ? undefined
                            : {
                                rotateX: -3,
                                rotateY: 4,
                                scale: 1.015,
                                transition: { duration: 0.4, ease: [0.25, 0.46, 0.45, 0.94] },
                              }
                        }
                      >
                        {/* Mini progress visualization per audience */}
                        {item.id === "01" && <AudienceCardBeginner />}
                        {item.id === "02" && <AudienceCardJunior />}
                        {item.id === "03" && <AudienceCardMiddle />}
                        {item.id === "04" && <AudienceCardCareer />}
                      </motion.div>
                    </div>
                  </div>
                </ScrollReveal>
              </div>
            ))}
          </div>
        </div>
      </div>
    </section>
  );
}

// ---------------------------------------------------------------------------
// Audience visual cards — mini product demos for each segment
// ---------------------------------------------------------------------------

function AudienceCardBeginner() {
  return (
    <div className="space-y-4">
      <p className="text-xs font-medium uppercase tracking-wider text-white/30">Темы этапа</p>
      <div className="space-y-3">
        {[
          "Архитектура и DDD",
          "ASP.NET Core — создание API",
          "PostgreSQL, EF Core — работа с БД",
          "S3 / MinIO — файловые хранилища",
          "RabbitMQ — брокеры сообщений",
          "React, Next.js, TypeScript",
          "Auth Service, JWT — аутентификация",
          "Docker, Nginx, CI/CD — деплой",
        ].map((step, i) => (
          <div key={i} className="flex items-center gap-3">
            <div className="flex h-6 w-6 flex-shrink-0 items-center justify-center rounded-full bg-[#6BADA5]/20 text-[10px] font-bold text-[#6BADA5]">
              {i + 1}
            </div>
            <span className="text-sm text-white/70">{step}</span>
            <span className="ml-auto text-xs text-[#6BADA5]">✓</span>
          </div>
        ))}
      </div>
      <div className="rounded-lg border border-[#6BADA5]/15 bg-[#6BADA5]/[0.04] px-3 py-2 text-center">
        <span className="text-xs text-[#6BADA5]">28 модулей • 93 урока • 5 проектов</span>
      </div>
    </div>
  );
}

function AudienceCardJunior() {
  return (
    <div className="space-y-3.5">
      <p className="text-xs font-medium uppercase tracking-wider text-white/30">
        Темы этапа — поверх базы Junior
      </p>
      <div className="space-y-2.5">
        {[
          { name: "Углублённый DDD и Clean Architecture", note: "стратегический + тактический" },
          { name: "CQRS и паттерны", note: "разделение чтения/записи, бизнес-паттерны" },
          { name: "Микросервисы", note: "разбиение монолита, контракты, обмен данными" },
          { name: "Тестирование и TDD", note: "юнит, интеграционные, моки" },
          { name: "Performance и concurrency", note: "блокировки, профилирование, N+1" },
          { name: "Observability", note: "логи, метрики, distributed tracing" },
        ].map((skill) => (
          <div key={skill.name} className="flex items-start gap-2.5">
            <span className="mt-1 text-[#6BADA5]">✓</span>
            <div>
              <p className="text-sm text-white/70">{skill.name}</p>
              <p className="text-xs text-white/35">{skill.note}</p>
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}

function AudienceCardMiddle() {
  return (
    <div className="space-y-4">
      <p className="text-xs font-medium uppercase tracking-wider text-white/30">Инженерные темы</p>
      <div className="space-y-2">
        {[
          "AI в разработке",
          "Продакшен-инфраструктура",
          "System Design",
          "Архитектурные решения",
          "Soft Skills",
        ].map((name) => (
          <div key={name} className="flex items-center gap-2.5 rounded-lg bg-[#0A0A0B] px-3 py-2.5">
            <span className="text-[#6BADA5]">✓</span>
            <span className="text-sm text-white/60">{name}</span>
          </div>
        ))}
      </div>
    </div>
  );
}

function AudienceCardCareer() {
  const items = [
    { label: "Резюме и портфолио", desc: "Упаковка опыта" },
    { label: "Поиск работы", desc: "Вакансии и отклики" },
  ];

  return (
    <div className="space-y-4">
      <p className="text-xs font-medium uppercase tracking-wider text-white/30">Карьерные шаги</p>
      <div className="space-y-2">
        {items.map((item) => (
          <div
            key={item.label}
            className="flex items-center justify-between rounded-lg bg-[#0A0A0B] px-3 py-2.5"
          >
            <div>
              <p className="text-sm font-medium text-white/70">{item.label}</p>
              <p className="text-xs text-white/30">{item.desc}</p>
            </div>
            <span
              className="ml-2 shrink-0 rounded-full px-2 py-0.5 text-[10px] font-medium"
              style={{
                color: "#C9A84C",
                backgroundColor: "#C9A84C18",
                border: "1px solid #C9A84C40",
              }}
            >
              NEW
            </span>
          </div>
        ))}
      </div>
      <div className="flex items-center gap-2 pt-1">
        <span className="relative flex h-2 w-2">
          <span
            className="absolute inline-flex h-full w-full animate-ping rounded-full opacity-60"
            style={{ backgroundColor: "#C9A84C" }}
          />
          <span
            className="relative inline-flex h-2 w-2 rounded-full"
            style={{ backgroundColor: "#C9A84C" }}
          />
        </span>
        <span className="text-xs" style={{ color: "#C9A84C" }}>
          Трек активно развивается
        </span>
      </div>
    </div>
  );
}
