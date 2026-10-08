import { testimonials } from "../config";
import { ScrollReveal } from "./scroll-reveal";
import { TestimonialCard } from "./testimonial-modal";
import { TestimonialsCarousel } from "./testimonials-carousel";

function TelegramIcon({ className }: { className?: string }) {
  return (
    <svg className={className} viewBox="0 0 24 24" fill="currentColor">
      <path d="M11.944 0A12 12 0 0 0 0 12a12 12 0 0 0 12 12 12 12 0 0 0 12-12A12 12 0 0 0 12 0a12 12 0 0 0-.056 0zm4.962 7.224c.1-.002.321.023.465.14a.506.506 0 0 1 .171.325c.016.093.036.306.02.472-.18 1.898-.962 6.502-1.36 8.627-.168.9-.499 1.201-.82 1.23-.696.065-1.225-.46-1.9-.902-1.056-.693-1.653-1.124-2.678-1.8-1.185-.78-.417-1.21.258-1.91.177-.184 3.247-2.977 3.307-3.23.007-.032.014-.15-.056-.212s-.174-.041-.249-.024c-.106.024-1.793 1.14-5.061 3.345-.48.33-.913.49-1.302.48-.428-.008-1.252-.241-1.865-.44-.752-.245-1.349-.374-1.297-.789.027-.216.325-.437.893-.663 3.498-1.524 5.83-2.529 6.998-3.014 3.332-1.386 4.025-1.627 4.476-1.635z" />
    </svg>
  );
}

export function TestimonialsSection() {
  const cards = testimonials.map((t, i) => (
    <TestimonialCard key={t.handle ?? `anon-${i}`} handle={t.handle} result={t.result} text={t.text}>
      {/* Card preview content — server rendered */}
      <span className="inline-flex rounded-full bg-[#6BADA5]/10 px-2.5 py-0.5 text-xs font-medium text-[#6BADA5]">
        {t.result}
      </span>
      <p className="mt-3 text-sm leading-relaxed text-white/50 line-clamp-5">
        {t.text}
      </p>
      <div className="mt-3 flex items-center gap-2 text-xs text-white/25">
        Читать полностью...
      </div>
      <div className="mt-3 flex items-center gap-2 border-t border-white/[0.04] pt-3">
        {t.handle ? (
          <>
            <TelegramIcon className="h-3.5 w-3.5 shrink-0 text-[#6BADA5]" />
            <span className="text-xs text-[#6BADA5]">@{t.handle}</span>
          </>
        ) : (
          <span className="text-xs text-white/40">Аноним</span>
        )}
      </div>
    </TestimonialCard>
  ));

  return (
    <section id="testimonials" className="relative py-10 md:py-16 lg:py-20">
      <div className="mx-auto max-w-7xl px-6">
        <ScrollReveal>
          <h2 className="text-center text-3xl font-bold tracking-tight sm:text-4xl md:text-5xl">
            Отзывы учеников
          </h2>
          <p className="mx-auto mt-4 max-w-xl text-center text-base text-white/50 md:text-lg">
            Реальные результаты — от первого оффера до 400к/мес
          </p>
        </ScrollReveal>
      </div>

      <div className="mt-10 lg:mt-14">
        <TestimonialsCarousel>{cards}</TestimonialsCarousel>

        {/* Scroll hint — mobile only */}
        <p className="mt-3 text-center text-xs text-white/20 lg:hidden">
          ← {testimonials.length} отзывов — листайте →
        </p>
      </div>
    </section>
  );
}
