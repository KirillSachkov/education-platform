import Link from "next/link";
import { Icons } from "@/shared/ui/icons";
import { routes } from "@/shared/config/routes";

/**
 * Минимальная секция-указатель: бесплатные материалы доступны в базе знаний.
 * Заменила предыдущий грид бесплатных материалов — теперь лендинг просто
 * ведёт читателя в КБ с фильтром `?free=1`, а не дублирует там же
 * подборку карточек.
 */
export function KnowledgeBaseLinkSection() {
  return (
    <section className="border-t border-white/[0.04] bg-[#0E0E11]">
      <div className="mx-auto max-w-3xl px-4 py-16 text-center sm:px-6 lg:px-8 lg:py-20">
        <p className="text-xs uppercase tracking-[0.18em] text-emerald-400/70">
          Без регистрации и оплаты
        </p>
        <h2 className="mt-3 text-2xl font-bold sm:text-3xl">Бесплатные материалы</h2>
        <p className="mt-3 text-sm text-white/60 sm:text-base">
          Доступны в базе знаний — статьи, видео и конспекты, которые можно почитать или посмотреть
          прямо сейчас.
        </p>
        <Link
          href={`${routes.knowledgeBase}?free=1`}
          data-growth-cta="free_materials"
          data-growth-placement="material"
          className="mt-8 inline-flex items-center gap-2 rounded-lg border border-emerald-500/30 bg-emerald-500/10 px-6 py-3 text-sm font-medium text-emerald-300 transition-colors hover:border-emerald-400/60 hover:bg-emerald-500/15 hover:text-emerald-200"
        >
          Открыть базу знаний
          <Icons.arrowRight size={14} />
        </Link>
      </div>
    </section>
  );
}
