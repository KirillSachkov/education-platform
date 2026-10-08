import type { Metadata } from "next";
import { LevelTestFunnel } from "@/features/level-test-runner";
import { APP_URL } from "@/shared/config/site";
import { routes } from "@/shared/config/routes";
import { QuizJsonLd } from "@/shared/seo";

const TITLE = "Тест уровня .NET-разработчика — SachkovLearn";
const DESCRIPTION =
  "Бесплатный тест уровня по C# и .NET: вопросы от основ до архитектуры, ~20 минут. Узнай свой уровень, получи разбор по темам и рекомендацию курса. Полная проверка ответов, включая развёрнутые.";

const PAGE_URL = `${APP_URL}${routes.levelTest}`;

export const metadata: Metadata = {
  title: TITLE,
  description: DESCRIPTION,
  alternates: { canonical: PAGE_URL },
  openGraph: {
    title: TITLE,
    description: DESCRIPTION,
    type: "website",
    url: PAGE_URL,
  },
  twitter: {
    card: "summary_large_image",
    title: TITLE,
    description: DESCRIPTION,
  },
};

/**
 * `/level-test` — публичная лендинг-страница воронки теста уровня (#481):
 * hero с CTA, затем прохождение inline (intro → questions → submitting).
 * Доступна анонимам — lead-gate срабатывает на странице результата.
 * SEO (#482): страница в sitemap + Quiz JSON-LD (server-rendered, без hasPart —
 * вопросы не перечисляем, они и есть продукт).
 */
export default function LevelTestPage() {
  return (
    <div className="px-4">
      <QuizJsonLd
        name="Определи свой уровень .NET-разработчика"
        description={DESCRIPTION}
        url={PAGE_URL}
        providerName="SachkovLearn"
        about=".NET"
      />
      <LevelTestFunnel />
    </div>
  );
}
