"use client";

import { useRef } from "react";
import { motion, useScroll, useTransform } from "framer-motion";
import { learningSteps } from "../config";
import { useReducedMotion } from "../hooks/use-reduced-motion";
import { ScrollReveal } from "./scroll-reveal";
import { LiveMockup } from "./live-mockups";

// ---------------------------------------------------------------------------
// LearningProcess — zigzag layout with center timeline
// ---------------------------------------------------------------------------

export function StickyScrollLearning() {
  const reduced = useReducedMotion();
  const containerRef = useRef<HTMLDivElement>(null);
  const { scrollYProgress } = useScroll({
    target: containerRef,
    offset: ["start 80%", "end 50%"],
  });
  const lineHeight = useTransform(scrollYProgress, [0, 1], ["0%", "100%"]);

  return (
    <section id="learning" className="relative py-10 md:py-16 lg:py-20">
      <div className="mx-auto max-w-7xl px-6">
        <ScrollReveal>
          <h2 className="text-center text-3xl font-bold tracking-tight sm:text-4xl md:text-5xl">
            Как устроено обучение
          </h2>
          <p className="mx-auto mt-4 max-w-2xl text-center text-base text-white/50 md:text-lg">
            Полный доступ .NET Fullstack — это не набор видео. Внутри задания и проекты, AI-ревью
            каждого PR, закрытый чат и ответы автора на вопросы.
          </p>
        </ScrollReveal>

        <div ref={containerRef} className="relative mt-12 md:mt-16">
          {/* Vertical timeline — left on mobile, center on desktop */}
          <div
            className="absolute left-0 top-0 h-full w-px lg:left-1/2 lg:-translate-x-1/2"
            style={{ backgroundColor: "rgba(255,255,255,0.04)" }}
          >
            <motion.div
              className="w-full origin-top"
              style={{
                height: reduced ? "100%" : lineHeight,
                background:
                  "linear-gradient(to bottom, transparent 0%, #6BADA580 2%, #6BADA5 8%, #6BADA5 60%, #C9A84C 85%, #C9A84C40 100%)",
              }}
            />
          </div>

          <div className="space-y-20 pl-5 sm:pl-6 md:space-y-28 lg:space-y-32 lg:pl-0">
            {learningSteps.map((step, i) => {
              const isReversed = i % 2 === 1;
              return (
                <ScrollReveal key={step.id}>
                  <div className="relative">
                    {/* Timeline dot — mobile left, desktop center */}
                    <div className="absolute -left-[13px] top-1 z-10 h-2.5 w-2.5 rounded-full border-2 border-[#0E0E11] bg-[#6BADA5] shadow-[0_0_8px_rgba(107,173,165,0.5)] sm:-left-[14px] lg:left-1/2 lg:top-6 lg:h-3 lg:w-3 lg:-translate-x-1/2" />

                    <div
                      className={`flex flex-col items-center gap-8 md:gap-12 ${
                        isReversed ? "lg:flex-row-reverse" : "lg:flex-row"
                      }`}
                    >
                      {/* Text */}
                      <div className="flex-1 space-y-3">
                        <div className="flex items-center gap-3">
                          <span className="text-xs font-medium text-white/25">
                            {String(i + 1).padStart(2, "0")}
                          </span>
                          <h3 className="text-2xl font-bold md:text-3xl">{step.title}</h3>
                        </div>
                        {step.badge && (
                          <span className="inline-flex rounded-full border border-[#6BADA5]/30 bg-[#6BADA5]/10 px-3 py-1 text-xs font-medium text-[#6BADA5]">
                            {step.badge}
                          </span>
                        )}
                        <p className="text-sm leading-relaxed text-white/50 sm:text-base lg:text-[15px]">
                          {step.description}
                          {step.descriptionGold && (
                            <>
                              {" "}
                              <span className="text-[#C9A84C]/70">{step.descriptionGold}</span>
                            </>
                          )}
                        </p>
                      </div>

                      {/* Mockup */}
                      <div className="flex w-full flex-1 items-center justify-center">
                        <div className="w-full max-w-md">
                          <LiveMockup id={step.id} isActive={true} />
                        </div>
                      </div>
                    </div>
                  </div>
                </ScrollReveal>
              );
            })}
          </div>
        </div>
      </div>
    </section>
  );
}
