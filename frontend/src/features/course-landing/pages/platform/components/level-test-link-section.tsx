import Link from "next/link";
import { Icons } from "@/shared/ui/icons";
import { routes } from "@/shared/config/routes";

/**
 * Секция-указатель на тест уровня (#528): лендинг ведёт читателя в воронку
 * `/level-test` — 33 вопроса от основ до архитектуры, уровень по 6-ступенчатой
 * шкале, разбор по темам и рекомендация курса.
 */
export function LevelTestLinkSection() {
  return (
    <section className="border-t border-white/[0.04] bg-[#0A0A0B]">
      <div className="mx-auto max-w-3xl px-4 py-16 text-center sm:px-6 lg:px-8 lg:py-20">
        <p className="text-xs uppercase tracking-[0.18em] text-[#6BADA5]/80">
          Бесплатный тест · ~20 минут
        </p>
        <h2 className="mt-3 text-2xl font-bold sm:text-3xl">
          Определи свой уровень .NET-разработчика
        </h2>
        <p className="mt-3 text-sm text-white/60 sm:text-base">
          Вопросы из реальных собеседований — от C# и асинхронности до SQL и микросервисов. На
          выходе — уровень от «Новичка» до Senior, слабые места и с какого курса начать.
        </p>
        <Link
          href={routes.levelTest}
          data-growth-cta="level_test"
          data-growth-placement="level_test"
          className="mt-8 inline-flex items-center gap-2 rounded-lg border border-[#6BADA5]/40 bg-[#6BADA5]/10 px-6 py-3 text-sm font-medium text-[#8CCFC6] transition-colors hover:border-[#6BADA5]/70 hover:bg-[#6BADA5]/15 hover:text-[#A8E0D8]"
        >
          Пройти тест уровня
          <Icons.arrowRight className="size-3.5" />
        </Link>
      </div>
    </section>
  );
}
