/**
 * Единый источник данных pricing — используется и в `features/course-landing`
 * (PlansShowcase на главном лендинге), и в `widgets/pricing-catalog` (полная
 * страница `/pricing` с comparison table и FAQ).
 *
 * Чисто статика — никаких запросов к бэкенду. При появлении новых планов
 * обновляется здесь и деплоится. Это намеренно: лендинг должен быть быстрым,
 * SEO-friendly, и не зависеть от доступности access-сервиса.
 */

import type { CourseKind } from "@/shared/config/course-kind";
import type { PlanOfferType } from "@/shared/config/offer-type";

export type PlanAccent = "neutral" | "teal" | "gold";

export interface PlanFeature {
  text: string;
  highlight?: boolean;
}

export type PlanCardKind = "free" | "paid" | "tbd";

export interface PlanCard {
  id: string;
  /**
   * Backend-планы имеют GUID — нужен для матча с PlanGrantDto.planId.
   * STATIC_PLANS на лендинге его не имеют (нет реального плана за карточкой).
   */
  planId?: string;
  badge: string;
  name: string;
  description: string;
  longDescription: string;
  price: string;
  /** Цена числом — для расчёта рассрочки и payment SDK. 0 = бесплатно. */
  priceAmount: number;
  /** Цена в копейках — для T-Bank checkout. Только для backend-планов с явной ценой. */
  priceCents?: number;
  /** Валюта — для T-Bank checkout. Default RUB. */
  currency?: string;
  /** Старая (зачёркнутая) цена, если активна акция. null/undefined — акции нет. */
  originalPriceLabel?: string | null;
  /** Процент акционной скидки — для бейджа «−N%». */
  discountPercent?: number | null;
  /** Хинт окончания акции «до 12 июня». */
  discountEndsHint?: string | null;
  priceNote: string;
  /** Доступная рассрочка (например «4 платежа по 28 000 ₽»). null = не доступна. */
  installmentLabel: string | null;
  features: PlanFeature[];
  /** Ключи строк comparison-таблицы, которые «есть» у этого плана. */
  comparisonKeys: string[];
  /** Текст CTA на лендинге («Получить бесплатно», «Получить полный доступ»). */
  cta: string;
  /** href CTA на лендинге — ведёт на `/pricing` чтобы юзер видел детали и оплачивал. */
  ctaHref: string;
  /** CTA на странице /pricing для платных планов («Оплатить 112 000 ₽»). */
  paymentCta: string;
  accent: PlanAccent;
  highlighted: boolean;
  /**
   * Тип карточки для PaymentButton:
   * - `free` — статический «бесплатный» offer на лендинге (CTA → регистрация)
   * - `paid` — оплата (показываем рассрочку + payment methods)
   * - `tbd` — цена не указана автором → CTA «Узнать цену» через Telegram
   *
   * Опционально для backward-compat со статическим массивом PLANS.
   */
  kind?: PlanCardKind;
  /** Backend plan tier — для группировки секций на странице «Доступ». Static-планы лендинга его не имеют. */
  tier?: "FULL_ALL" | "LEARN_ALL" | "COURSE" | "SUBSCRIPTION" | "FREE";
  /**
   * Маркетинг-формат оффера (#418/#614) — драйвер группировки секций. Для подписки
   * Trainer Pro = `TRAINER_PRO`: рендерится отдельной секцией «Тренажёр». Static-планы
   * лендинга его не имеют.
   */
  offerType?: PlanOfferType;
  /**
   * Интервал автопродления подписки в днях (#614) — для лейбла «₽X / мес».
   * null/undefined для разовых (LIFETIME) планов.
   */
  termRecurringDays?: number | null;
  /** Первый курс плана (legacy) — для single-course карточки. Bundle берёт `includedCourses`. */
  courseId?: string | null;
  /** Полный список id курсов плана (bundle, #404). */
  courseIds?: string[];
  /** Курсы плана с slug + kind (bundle, #404) — для clickable-чипов и kind-бейджа. */
  includedCourses?: { id: string; title: string; slug: string; kind: CourseKind }[] | null;
  /**
   * Длительность пробного доступа в днях (#580). >0 → это «Пробный месяц»:
   * платный план с временным полным доступом. Static-планы лендинга его не имеют.
   */
  trialDurationDays?: number | null;
}

/**
 * Общая ссылка на Telegram-консультацию автора. Заглушка для CTA «купить»
 * до тех пор, пока не подключена реальная платёжная интеграция.
 */
import { PRIMARY_AUTHOR_CONSULTATION_LINK } from "@/shared/config/primary-author";

export const PRICING_CTA_HREF = PRIMARY_AUTHOR_CONSULTATION_LINK;

// Issue #358: «Бесплатный» план больше не является отдельным offering'ом —
// бесплатные материалы доступны любому залогиненному юзеру (AccessType.REGISTERED).
// На лендинге это первая карточка-приглашение к регистрации.
export const PLANS: PlanCard[] = [
  {
    id: "free",
    badge: "Знакомство",
    name: "Бесплатный доступ",
    description:
      "Открытые материалы направления .NET Fullstack и базовая навигация после регистрации",
    longDescription:
      "После регистрации открываются демо-уроки, обзорные статьи и навигация по обучению .NET Fullstack. Без оплаты и без срока действия.",
    price: "0 ₽",
    priceAmount: 0,
    priceNote: "после регистрации",
    installmentLabel: null,
    features: [
      { text: "Демо-уроки в каждом курсе" },
      { text: "Навигация по обучению .NET Fullstack" },
      { text: "Тематические материалы" },
      { text: "Без ограничения по времени" },
    ],
    comparisonKeys: ["demo_lessons", "platform_access"],
    cta: "Зарегистрироваться",
    ctaHref: `/pricing#free`,
    paymentCta: "Зарегистрироваться",
    accent: "neutral",
    highlighted: false,
  },
  {
    id: "lifetime",
    badge: "Единственный тариф",
    name: "Полный доступ .NET Fullstack",
    description:
      "Вся программа .NET Fullstack — оба уровня, текущие и будущие материалы. AI-ревью PR, закрытый чат и поддержка автора. Доступ без срока",
    longDescription:
      "Полный доступ к программе .NET Fullstack: курсы обоих уровней, задания, проекты и тесты, AI-ревью каждого PR, закрытый Telegram-чат, ответы автора на вопросы и помощь с резюме и поиском работы. Новые материалы программы добавляются без доплаты. Оплата один раз, подписки нет.",
    price: "112 000 ₽",
    priceAmount: 112000,
    priceNote: "разовая оплата · доступ навсегда",
    installmentLabel: "4 платежа по 28 000 ₽",
    features: [
      { text: "Все курсы программы .NET Fullstack — текущие и будущие", highlight: true },
      { text: "Задания, проекты и тесты" },
      { text: "AI-ревью каждого PR с заданием", highlight: true },
      { text: "Закрытый Telegram-чат учеников" },
      { text: "Ответы автора на вопросы по программе" },
      { text: "Помощь с резюме и поиском работы" },
      { text: "Доступ без срока, без подписки" },
    ],
    comparisonKeys: [
      "demo_lessons",
      "platform_access",
      "all_courses",
      "tasks_projects",
      "ai_review",
      "community",
      "author_support",
      "job_help",
      "lifetime_access",
    ],
    cta: "Получить полный доступ",
    ctaHref: `/pricing#lifetime`,
    paymentCta: "Оплатить 112 000 ₽",
    accent: "gold",
    highlighted: true,
  },
];

/**
 * Comparison-таблица для full pricing page. Группирует фичи по разделам;
 * каждая строка — `key` соответствует `comparisonKeys` плана.
 */
export interface ComparisonRow {
  key: string;
  label: string;
  hint?: string;
}

export interface ComparisonGroup {
  title: string;
  rows: ComparisonRow[];
}

export const COMPARISON_GROUPS: ComparisonGroup[] = [
  {
    title: "Старт",
    rows: [
      { key: "platform_access", label: "Доступ к обучению .NET Fullstack" },
      { key: "demo_lessons", label: "Демо-уроки в каждом курсе" },
    ],
  },
  {
    title: "Программа",
    rows: [
      { key: "all_courses", label: "Все курсы программы — видео и материалы" },
      { key: "tasks_projects", label: "Задания и проекты", hint: "Практика на реальных кейсах" },
      { key: "ai_review", label: "AI-ревью каждого PR", hint: "Автоматическая проверка решения" },
    ],
  },
  {
    title: "Поддержка",
    rows: [
      { key: "community", label: "Закрытый Telegram-чат" },
      { key: "author_support", label: "Ответы автора на вопросы" },
      { key: "job_help", label: "Помощь с резюме и поиском работы" },
      { key: "lifetime_access", label: "Доступ без срока" },
    ],
  },
];

export interface PricingFaqItem {
  question: string;
  answer: string;
  /** Показать кликабельную ссылку «Написать в Telegram» под ответом. */
  telegramCta?: boolean;
}

export const PRICING_FAQ: PricingFaqItem[] = [
  {
    question: "Software Engineer — это отдельное направление, не .NET?",
    answer:
      "Нет. Это тот же .NET и C#, просто продвинутый уровень. Фундамент выводит на junior на .NET-стеке, Software Engineer углубляет тот же стек до production/senior: архитектура, микросервисы, DevOps, AI, безопасность. Deploy, DevOps и фронтенд — это ветви вокруг .NET-ядра, всё, что нужно backend-инженеру. Полный доступ открывает оба уровня программы .NET Fullstack.",
  },
  {
    question: "Что я получу после оплаты?",
    answer:
      "Всю программу .NET Fullstack: курсы обоих уровней, задания, проекты и тесты, AI-ревью PR с заданиями, закрытый Telegram-чат и ответы автора на вопросы. Оплата проходит через Т-Банк, доступ открывается сразу после оплаты. По любым вопросам — напишите в Telegram.",
    telegramCta: true,
  },
  {
    question: "Есть ли потоки? Когда можно начать?",
    answer:
      "Потоков и наборов нет. После оплаты доступ открывается сразу: все материалы, задания и AI-ревью доступны с первого дня — ждать группу не нужно. Темп выбираете сами.",
  },
  {
    question: "Как проверяются задания?",
    answer:
      "Решение задания вы отправляете pull request'ом на GitHub. AI-ревью разбирает код и оставляет замечания прямо в PR, можно исправлять и отправлять снова. Если вопрос остался — задайте его автору в задании или в закрытом чате.",
  },
  {
    question: "Хватит ли навыков, чтобы устроиться на первую работу?",
    answer:
      "Программа построена именно под это. Если знания пока «пассивные» и собраны по туториалам, упор на практике это меняет: проекты, задания и AI-ревью вашего кода превращают теорию в рабочие навыки. С резюме и поиском работы автор поможет по запросу. Результат зависит от вашей практики, трудоустройство мы не гарантируем.",
  },
  {
    question: "Можно ли вернуть деньги?",
    answer:
      "Да. В течение 3 дней после оплаты вернём полную стоимость без объяснения причин — просто напишите в Telegram. Позже возврат тоже возможен: из суммы вычитается стоимость уже открытых материалов по расчёту из оферты.",
    telegramCta: true,
  },
  {
    question: "Доступна ли рассрочка? Как происходит оплата?",
    answer:
      "Оплата проходит только через платёжную систему Т-Банка — напрямую автору переводить не нужно. На странице оплаты можно выбрать «Долями» (несколько платежей без переплаты) или оформить банковскую рассрочку; точные условия, срок и график платежей Т-Банк покажет там же. Если активна скидка — она уже учтена в сумме, которую делят на части. Нужен другой способ (зарубежная карта, крипта) — напишите в Telegram.",
    telegramCta: true,
  },
  {
    question: "Можно ли оплатить криптовалютой или зарубежной картой?",
    answer:
      "Да. Доступна оплата зарубежной картой (не из всех стран) и полная оплата криптовалютой. Напишите в Telegram — подберём удобный способ.",
    telegramCta: true,
  },
  {
    question: "Я уже покупал курс или интенсив. Можно доплатить до полного доступа?",
    answer:
      "Да. Стоимость уже купленных курсов и интенсивов вычитается из цены полного доступа. Залогиньтесь и откройте страницу тарифов — на карточке «Полный доступ» будет персональная цена с учётом вычета. Купленный ранее доступ сохраняется.",
    telegramCta: true,
  },
  {
    question: "Что если в программе появятся новые материалы?",
    answer:
      "Новые курсы и материалы программы .NET Fullstack, которые выходят после оплаты, добавляются в полный доступ без доплаты. Подписки и продления нет.",
  },
];
