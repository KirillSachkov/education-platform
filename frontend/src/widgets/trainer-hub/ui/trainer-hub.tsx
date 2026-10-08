"use client";

import { trainerQuestionsQueryOptions } from "@/entities/trainer-question";
import { trainerSessionQueryOptions } from "@/entities/trainer-session";
import { trainerTopicsQueryOptions, type TrainerTopicListItem } from "@/entities/trainer-topic";
import { trainerTracksQueryOptions, type TrainerTrack } from "@/entities/trainer-track";
import { SrsReviewBanner } from "@/features/trainer-srs-review";
import { TrainerStatsDashboard } from "@/features/trainer-statistics";
import { TrainerProStatusCard } from "@/features/trainer-pro-status";
import { useStartReview } from "@/features/start-review-session";
import { getErrorMessage } from "@/shared/api";
import { useIsAuthenticated } from "@/shared/auth";
import { routes } from "@/shared/config/routes";
import { TRAINER_STACK_LABELS } from "@/shared/config/trainer";
import { cn } from "@/shared/lib/css";
import { pluralize } from "@/shared/lib/pluralize";
import { AnimatedNumber, type SegmentedOption } from "@/shared/ui/components";
import { Icons } from "@/shared/ui/icons";
import { Button } from "@/shared/ui/kit/button";
import { EmptyState } from "@/shared/ui/kit/empty-state";
import { Skeleton } from "@/shared/ui/kit/skeleton";
import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import {
  HUB_PARAM,
  HUB_TABS,
  parseHubTab,
  parseStudySubmode,
  type HubTab,
  type StudySubmode,
} from "../lib/hub-state";
import { buildTrainerHubStats, getWeakTrainerTopics } from "../lib/hub-metrics";
import { useTrainerLoginGate } from "../lib/use-trainer-login-gate";
import { HubBookmarksTab } from "./hub-bookmarks-tab";
import { HubMistakesTab } from "./hub-mistakes-tab";
import { HubMockTab } from "./hub-mock-tab";
import { HubStudyTab } from "./hub-study-tab";
import { HubSubscribeCta } from "./hub-subscribe-cta";
import { HubTopicPicker } from "./hub-topic-picker";

const TAB_META: Record<HubTab, { label: string; icon: typeof Icons.graduation }> = {
  study: { label: "Обучение", icon: Icons.graduation },
  mock: { label: "Симуляция", icon: Icons.briefcase },
  progress: { label: "Статистика", icon: Icons.chart },
  mistakes: { label: "Ошибки", icon: Icons.warning },
  bookmarks: { label: "Закладки", icon: Icons.bookmark },
};

const HUB_SRS_DUE_LIMIT = 50;
const ALL_TRACKS_VALUE = "all";

/**
 * Хаб тренажёра `/trainer` (#568, Ф2) — единая страница, mode-centric: верхний
 * селектор трека + фильтр направления → стрик-индикатор → SRS-баннер «на повтор»
 * → шесть вкладок (Изучение · Тесты · Mock · Прогресс · Ошибки · Закладки).
 * Состояние верхнего уровня — в URL (`?track=&dir=&tab=&topic=&sub=`), deep-link и
 * «назад» работают. Прогон сессии (тест/mock/learn) живёт на `/trainer/session/{id}`;
 * флеш-карты (изучение/повтор/доучивание ошибок) — оверлеем поверх вкладок.
 */
export function TrainerHub() {
  const isAuthenticated = useIsAuthenticated();
  const tracksQuery = useQuery(trainerTracksQueryOptions.tracksOptions());

  if (tracksQuery.isPending) {
    return <HubSkeleton />;
  }

  if (tracksQuery.isError) {
    return (
      <div className="mx-auto w-full max-w-5xl p-4 md:p-6">
        <EmptyState
          icon={Icons.error}
          variant="card"
          title="Не удалось загрузить тренажёр"
          description={getErrorMessage(tracksQuery.error, "Попробуйте обновить страницу")}
          action={
            <Button variant="outline" onClick={() => tracksQuery.refetch()}>
              <Icons.refresh className="size-4" />
              Попробовать снова
            </Button>
          }
        />
      </div>
    );
  }

  const tracks = tracksQuery.data;

  if (tracks.length === 0) {
    return (
      <div className="mx-auto w-full max-w-5xl p-4 md:p-6">
        <EmptyState
          icon={Icons.compass}
          variant="card"
          title="Тренажёр готовится"
          description="Скоро здесь появятся треки и темы для подготовки к собеседованию — загляни позже."
        />
      </div>
    );
  }

  return <HubContent tracks={tracks} isAuthenticated={isAuthenticated} />;
}

function HubContent({
  tracks,
  isAuthenticated,
}: {
  tracks: TrainerTrack[];
  isAuthenticated: boolean;
}) {
  const router = useRouter();
  const pathname = usePathname();
  const searchParams = useSearchParams();

  // Гейт действий: аноним просматривает хаб read-only, но любое действие ведёт на логин (#614 F).
  const { requireAuth } = useTrainerLoginGate();

  // Запуск ТЕСТА (review-сессия) по набору вопросов — «Доучить» / SRS / клик по вопросу /
  // «Пройти тест по закладке». Стартуем сессию и навигируем на раннер (тест, не самопроверка).
  // Аноним → подсказка + логин (action-гейт), без вызова мутации.
  const startReview = useStartReview();
  const launchReviewTest = (questionIds: string[]) => {
    if (!requireAuth()) return;
    if (startReview.isPending || questionIds.length === 0) return;
    // «К теме» из review-сессии возвращает ровно во вкладку хаба, откуда запустили
    // (а не в выведенную из mode «Тренировку»); ctx=review снимает ярлык «Тренировка» (#656).
    const qs = searchParams.toString();
    const back = qs ? `${routes.trainer}?${qs}` : routes.trainer;
    startReview.mutate(
      { questionIds },
      {
        onSuccess: (session) =>
          router.push(
            `${routes.trainerSession(session.id)}?ctx=review&back=${encodeURIComponent(back)}`,
          ),
      },
    );
  };

  // Все темы (без фильтра) для topicMap — резолв названий в прогрессе/ошибках/
  // выбранной теме. React Query дедупит этот ключ с прогресс-вкладкой.
  const allTopicsQuery = useQuery(trainerTopicsQueryOptions.topicsOptions());
  const topicMap = new Map<string, TrainerTopicListItem>(
    (allTopicsQuery.data ?? []).map((topic) => [topic.id, topic]),
  );
  const trackSlugParam = searchParams.get(HUB_PARAM.track);
  const activeTrack = tracks.find((track) => track.slug === trackSlugParam);
  const activeTrackSlug = activeTrack?.slug ?? ALL_TRACKS_VALUE;
  const activeTrackId = activeTrack?.id;

  const scopedTopicsQuery = useQuery({
    ...trainerTopicsQueryOptions.topicsOptions(activeTrackId ? { trackId: activeTrackId } : {}),
    enabled: Boolean(activeTrackId),
  });
  const progressQuery = useQuery({
    ...trainerTopicsQueryOptions.progressOptions(),
    enabled: isAuthenticated,
  });
  const historyQuery = useQuery({
    ...trainerSessionQueryOptions.historyOptions(activeTrackId ? { trackId: activeTrackId } : {}),
    enabled: isAuthenticated,
  });

  const tab = parseHubTab(searchParams.get(HUB_PARAM.tab));
  const selectedTopicId = searchParams.get(HUB_PARAM.topic);
  const submode = parseStudySubmode(searchParams.get(HUB_PARAM.sub));
  const mastery = progressQuery.data?.mastery ?? [];
  const history = historyQuery.data ?? [];
  const isTrackScopeLoading = Boolean(activeTrackId && scopedTopicsQuery.isPending);
  const scopedTopicIds = activeTrackId
    ? new Set((scopedTopicsQuery.data ?? []).map((topic) => topic.id))
    : null;
  const scopedMastery = scopedTopicIds
    ? mastery.filter((row) => scopedTopicIds.has(row.topicId))
    : mastery;
  const availableTopicCount = activeTrackId
    ? (scopedTopicsQuery.data?.length ?? 0)
    : (allTopicsQuery.data?.length ?? 0);
  const hubStats = buildTrainerHubStats(scopedMastery, history, availableTopicCount);
  const weakTopics = getWeakTrainerTopics(scopedMastery, topicMap);
  const reviewTopicCount = scopedMastery.filter((row) => row.mistakesCount > 0).length;

  /**
   * Сменить query-параметры (shallow, через history.replace — не плодит записи
   * в истории на каждый клик, но deep-link/назад работают). Плавность контента
   * даёт CSS-fade на `TabsContent`. `null` удаляет параметр.
   */
  const setParams = (next: Partial<Record<keyof typeof HUB_PARAM, string | null>>) => {
    const params = new URLSearchParams(searchParams.toString());
    for (const [key, value] of Object.entries(next)) {
      const paramName = HUB_PARAM[key as keyof typeof HUB_PARAM];
      if (value === null) params.delete(paramName);
      else params.set(paramName, value);
    }
    const query = params.toString();
    router.replace(query ? `${pathname}?${query}` : pathname, { scroll: false });
  };

  const trackOptions: SegmentedOption<string>[] = [
    { value: ALL_TRACKS_VALUE, label: "Все" },
    ...tracks.map((track) => ({
      value: track.slug,
      label: TRAINER_STACK_LABELS[track.stack] ?? track.title,
    })),
  ];

  // Сменить трек — сбрасываем выбранную тему (она принадлежала прежнему треку).
  const handleTrackChange = (slug: string) =>
    setParams({ track: slug === ALL_TRACKS_VALUE ? null : slug, topic: null, sub: null });
  // Сменить вкладку — сбрасываем topic/sub (актуальны только для study/test).
  const handleTabChange = (value: HubTab) => setParams({ tab: value, topic: null, sub: null });

  /** Общий пикер темы для вкладок «Изучение»/«Тесты» (scope = трек). */
  const renderTopicPicker = (props: {
    title: string;
    hint?: string;
    hideLocked?: boolean;
    onPick: (topic: TrainerTopicListItem) => void;
  }) => (
    <HubTopicPicker
      isAuthenticated={isAuthenticated}
      hideLocked={props.hideLocked}
      title={props.title}
      hint={props.hint}
      trackId={activeTrack?.id}
      onPick={props.onPick}
    />
  );

  return (
    <div className="mx-auto w-full max-w-[1500px] space-y-4 p-4 md:p-6 xl:px-8">
      {isAuthenticated ? (
        <HubPageHeader
          trackOptions={trackOptions}
          activeTrackSlug={activeTrackSlug}
          onTrackChange={handleTrackChange}
        />
      ) : (
        <HubHero isAuthenticated={isAuthenticated} />
      )}

      <div className="grid gap-5 xl:grid-cols-[minmax(0,1fr)_300px] xl:items-start">
        <main className="min-w-0 space-y-5">
          {/* Subscribe-CTA над вкладками (#614): залогиненному без PRO предлагаем
              оформить подписку Trainer Pro. Сам компонент гейтит видимость. */}
          <HubSubscribeCta />

          <SrsReviewBanner
            isAuthenticated={isAuthenticated}
            topicIds={scopedTopicIds}
            isScopeLoading={isTrackScopeLoading}
            onStartReview={launchReviewTest}
            isLaunching={startReview.isPending}
          />

          <div className="space-y-4">
            <HubTabNavigation tab={tab} onTabChange={handleTabChange} />

            <div className="animate-in fade-in-0 duration-200" key={tab}>
              {tab === "study" && (
                <HubStudyTab
                  isAuthenticated={isAuthenticated}
                  selectedTopicId={selectedTopicId}
                  submode={submode}
                  topicMap={topicMap}
                  // Выбор/сброс темы сохраняет под-режим (формат): deep-link `?sub=test`
                  // ведёт в пикер и остаётся тестом после выбора темы.
                  onSelectTopic={(topicId) => setParams({ topic: topicId })}
                  onClearTopic={() => setParams({ topic: null })}
                  onSubmodeChange={(next: StudySubmode) =>
                    setParams({ sub: next === "list" ? null : next })
                  }
                  onLaunchTest={launchReviewTest}
                  renderTopicPicker={renderTopicPicker}
                />
              )}
              {tab === "mock" && <HubMockTab isAuthenticated={isAuthenticated} />}
              {tab === "progress" && <TrainerStatsDashboard isAuthenticated={isAuthenticated} />}
              {tab === "mistakes" && (
                <HubMistakesTab
                  isAuthenticated={isAuthenticated}
                  topicMap={topicMap}
                  topicIds={scopedTopicIds}
                  isScopeLoading={isTrackScopeLoading}
                  onLaunchTest={launchReviewTest}
                  isLaunching={startReview.isPending}
                />
              )}
              {tab === "bookmarks" && (
                <HubBookmarksTab
                  isAuthenticated={isAuthenticated}
                  topicIds={scopedTopicIds}
                  isScopeLoading={isTrackScopeLoading}
                  onLaunchTest={launchReviewTest}
                  isLaunching={startReview.isPending}
                />
              )}
            </div>
          </div>
        </main>

        <HubInsightPanel
          isAuthenticated={isAuthenticated}
          stats={hubStats}
          weakTopics={weakTopics}
          activeTrackSlug={activeTrackSlug}
          topicIds={scopedTopicIds}
          reviewTopicCount={reviewTopicCount}
          isLoading={
            isAuthenticated &&
            (progressQuery.isPending ||
              historyQuery.isPending ||
              allTopicsQuery.isPending ||
              isTrackScopeLoading)
          }
        />
      </div>
    </div>
  );
}

function HubPageHeader({
  trackOptions,
  activeTrackSlug,
  onTrackChange,
}: {
  trackOptions: SegmentedOption<string>[];
  activeTrackSlug: string;
  onTrackChange: (slug: string) => void;
}) {
  return (
    <header className="flex min-w-0 items-center">
      <div
        role="group"
        aria-label="Выбор трека"
        className="-mx-1 flex max-w-full items-center gap-4 overflow-x-auto px-1 py-1 [scrollbar-width:none] [&::-webkit-scrollbar]:hidden"
      >
        <span className="shrink-0 text-[11px] font-semibold uppercase tracking-[0.14em] text-muted-foreground">
          Трек
        </span>
        <div className="flex items-center gap-5">
          {trackOptions.map((option) => {
            const isActive = option.value === activeTrackSlug;

            return (
              <button
                key={option.value}
                type="button"
                aria-pressed={isActive}
                onClick={() => onTrackChange(option.value)}
                className={cn(
                  "relative min-h-8 shrink-0 rounded-md px-0.5 text-sm font-medium transition-colors",
                  "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2",
                  isActive ? "text-foreground" : "text-muted-foreground hover:text-foreground",
                )}
              >
                {option.label}
                <span
                  aria-hidden="true"
                  className={cn(
                    "absolute inset-x-0 -bottom-0.5 h-px rounded-full bg-primary transition-opacity",
                    isActive ? "opacity-100" : "opacity-0",
                  )}
                />
              </button>
            );
          })}
        </div>
      </div>
    </header>
  );
}

function HubTabNavigation({
  tab,
  onTabChange,
}: {
  tab: HubTab;
  onTabChange: (tab: HubTab) => void;
}) {
  return (
    <nav
      aria-label="Разделы тренажёра"
      // На десктопе разделы живут в TrainerSidebar (#623) — in-page таб-бар только на мобиле,
      // где сайдбар свёрнут в Sheet.
      className="-mx-1 overflow-x-auto px-1 [scrollbar-width:none] md:hidden [&::-webkit-scrollbar]:hidden"
    >
      <div
        role="tablist"
        className="flex w-max min-w-full items-center gap-1 rounded-xl border border-border/60 bg-card/70 p-1"
      >
        {HUB_TABS.map((key) => {
          const isActive = key === tab;
          const Icon = TAB_META[key].icon;
          return (
            <button
              key={key}
              type="button"
              role="tab"
              aria-selected={isActive}
              onClick={() => onTabChange(key)}
              className={cn(
                "inline-flex min-h-[44px] shrink-0 items-center gap-2 rounded-lg border px-3 text-sm font-medium transition-colors",
                "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-1",
                isActive
                  ? "border-primary/25 bg-primary/10 text-primary"
                  : "border-transparent text-muted-foreground hover:bg-muted hover:text-foreground",
              )}
            >
              <Icon className="size-4" />
              {TAB_META[key].label}
            </button>
          );
        })}
      </div>
    </nav>
  );
}

function HubInsightPanel({
  isAuthenticated,
  stats,
  weakTopics,
  activeTrackSlug,
  topicIds,
  reviewTopicCount,
  isLoading,
}: {
  isAuthenticated: boolean;
  stats: ReturnType<typeof buildTrainerHubStats>;
  weakTopics: ReturnType<typeof getWeakTrainerTopics>;
  activeTrackSlug: string;
  topicIds: Set<string> | null;
  reviewTopicCount: number;
  isLoading: boolean;
}) {
  const dueQuery = useQuery({
    ...trainerQuestionsQueryOptions.srsOptions(HUB_SRS_DUE_LIMIT),
    enabled: isAuthenticated,
  });
  const dueCount = topicIds
    ? (dueQuery.data ?? []).filter((item) => topicIds.has(item.topicId)).length
    : (dueQuery.data?.length ?? 0);
  const testHref = buildTrainerTabHref("study", activeTrackSlug, "test");
  const mistakesHref = buildTrainerTabHref("mistakes", activeTrackSlug);
  const mockHref = buildTrainerTabHref("mock", activeTrackSlug);

  if (!isAuthenticated) {
    return (
      <aside className="space-y-4 xl:sticky xl:top-24 xl:max-h-[calc(100vh-7rem)] xl:overflow-y-auto xl:pr-1">
        <InsightCard title="Твой прогресс">
          <p className="text-sm leading-relaxed text-muted-foreground">
            Войди, чтобы видеть темы к повтору, историю тренировок и повторение на сегодня.
          </p>
          <Button asChild className="mt-4 w-full" size="sm">
            <Link href={`${routes.login}?callbackUrl=${encodeURIComponent(routes.trainer)}`}>
              Войти
            </Link>
          </Button>
        </InsightCard>
      </aside>
    );
  }

  if (isLoading) {
    return (
      <aside className="space-y-4 xl:sticky xl:top-24 xl:max-h-[calc(100vh-7rem)] xl:overflow-y-auto xl:pr-1">
        <Skeleton className="h-44 w-full rounded-xl" />
        <Skeleton className="h-52 w-full rounded-xl" />
      </aside>
    );
  }

  return (
    <aside className="space-y-4 xl:sticky xl:top-24 xl:max-h-[calc(100vh-7rem)] xl:overflow-y-auto xl:pr-1">
      <TrainerProStatusCard />

      <InsightCard title="Статистика">
        <div className="space-y-3">
          <InsightRow icon={Icons.checkAll} label="Всего ответов" value={stats.answeredCount} />
          <InsightRow
            icon={Icons.completed}
            label="Завершено сессий"
            value={stats.completedSessions}
          />
          <InsightRow
            icon={Icons.percent}
            label="Средняя точность"
            value={
              stats.averageScorePercent === null ? "нет данных" : `${stats.averageScorePercent}%`
            }
          />
          <InsightRow
            icon={Icons.warning}
            label="Нужно подтянуть"
            value={stats.weakCount}
            tone={stats.weakCount > 0 ? "warn" : "good"}
          />
        </div>
      </InsightCard>

      <InsightCard title="Нужно подтянуть" action={<Link href={mistakesHref}>Все</Link>}>
        {weakTopics.length > 0 ? (
          <ul className="space-y-3">
            {weakTopics.map((topic) => (
              <li key={topic.topicId}>
                <Link
                  href={routes.trainerTopic(topic.slug)}
                  className="block rounded-lg border border-border/50 bg-background/35 p-3 transition-colors hover:bg-muted focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-1"
                >
                  <div className="flex items-center justify-between gap-3">
                    <span className="min-w-0 truncate text-sm font-medium">{topic.title}</span>
                    <span className="shrink-0 text-xs tabular-nums text-amber-600 dark:text-amber-400">
                      {topic.masteryPercent}%
                    </span>
                  </div>
                  <div className="mt-2 h-1.5 overflow-hidden rounded-full bg-border/45">
                    <div
                      className="h-full rounded-full bg-amber-500/80"
                      style={{ width: `${Math.max(topic.masteryPercent, 4)}%` }}
                    />
                  </div>
                </Link>
              </li>
            ))}
          </ul>
        ) : (
          <p className="text-sm leading-relaxed text-muted-foreground">
            Тем для подтягивания пока нет. Продолжай тренировки, чтобы статистика стала точнее.
          </p>
        )}
      </InsightCard>

      <InsightCard title="Маршрут">
        <div className="space-y-2">
          <InsightAction
            href={testHref}
            icon={Icons.listChecks}
            label="Пройти тест"
            meta={formatTopicCount(stats.topicCount)}
          />
          <InsightAction
            href={mistakesHref}
            icon={Icons.warning}
            label="Разобрать ошибки"
            meta={reviewTopicCount > 0 ? formatTopicCount(reviewTopicCount) : "чисто"}
          />
          <InsightAction
            href={mockHref}
            icon={Icons.briefcase}
            label="Симуляция"
            meta={dueCount > 0 ? `${dueCount} на повтор` : "готово"}
          />
        </div>
      </InsightCard>
    </aside>
  );
}

function buildTrainerTabHref(tab: HubTab, activeTrackSlug: string, sub?: string): string {
  const params = new URLSearchParams({ [HUB_PARAM.tab]: tab });
  if (activeTrackSlug !== ALL_TRACKS_VALUE) {
    params.set(HUB_PARAM.track, activeTrackSlug);
  }
  if (sub) {
    params.set(HUB_PARAM.sub, sub);
  }
  return `${routes.trainer}?${params.toString()}`;
}

function formatTopicCount(count: number): string {
  return `${count} ${pluralize(count, "тема", "темы", "тем")}`;
}

function InsightCard({
  title,
  action,
  children,
}: {
  title: string;
  action?: React.ReactNode;
  children: React.ReactNode;
}) {
  return (
    <section className="rounded-xl border border-border/60 bg-card/70 p-4 shadow-sm">
      <div className="mb-3 flex items-center justify-between gap-3">
        <h2 className="text-sm font-semibold tracking-tight">{title}</h2>
        {action && <div className="text-xs font-medium text-primary">{action}</div>}
      </div>
      {children}
    </section>
  );
}

function InsightRow({
  icon: Icon,
  label,
  value,
  tone = "default",
}: {
  icon: typeof Icons.check;
  label: string;
  value: number | string;
  tone?: "default" | "good" | "warn";
}) {
  return (
    <div className="flex items-center gap-3">
      <span
        className={cn(
          "grid size-8 shrink-0 place-items-center rounded-lg bg-muted text-muted-foreground",
          tone === "good" && "bg-green/10 text-green",
          tone === "warn" && "bg-amber-500/10 text-amber-600 dark:text-amber-400",
        )}
      >
        <Icon className="size-4" />
      </span>
      <span className="min-w-0 flex-1 text-sm text-muted-foreground">{label}</span>
      <span className="shrink-0 text-sm font-semibold tabular-nums">
        {typeof value === "number" ? <AnimatedNumber value={value} /> : value}
      </span>
    </div>
  );
}

function InsightAction({
  href,
  icon: Icon,
  label,
  meta,
}: {
  href: string;
  icon: typeof Icons.check;
  label: string;
  meta: string;
}) {
  return (
    <Link
      href={href}
      className="flex min-h-[44px] items-center gap-3 rounded-lg border border-border/50 bg-background/35 px-3 py-2 transition-colors hover:bg-muted focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-1"
    >
      <Icon className="size-4 shrink-0 text-primary" />
      <span className="min-w-0 flex-1 text-sm font-medium">{label}</span>
      <span className="shrink-0 text-xs text-muted-foreground">{meta}</span>
    </Link>
  );
}

function HubHero({ isAuthenticated }: { isAuthenticated: boolean }) {
  return (
    <section className="relative overflow-hidden rounded-2xl border border-border/60 bg-card/70 p-6 shadow-sm sm:p-8">
      <div
        aria-hidden="true"
        className="pointer-events-none absolute right-8 top-1/2 hidden -translate-y-1/2 lg:block"
      >
        <HeroMotif />
      </div>
      <div className="relative flex max-w-2xl flex-col gap-3">
        <span className="text-[11px] font-semibold uppercase tracking-[0.16em] text-primary">
          Тренажёр собеседований
        </span>
        <h1 className="text-2xl font-bold tracking-tight text-balance sm:text-3xl">
          Готовься к собеседованию на практике
        </h1>
        <p className="text-pretty text-sm leading-relaxed text-muted-foreground sm:text-base">
          Изучай вопросы карточками, проходи проверочные тесты и симуляции собеседований, следи за
          освоением тем и доучивай ошибки. Всё в одном месте.
        </p>
        {!isAuthenticated && (
          <div className="flex flex-wrap items-center gap-3 pt-1">
            <Button asChild>
              <Link href={`${routes.login}?callbackUrl=${encodeURIComponent(routes.trainer)}`}>
                Войти и тренироваться
              </Link>
            </Button>
            <p className="text-xs text-muted-foreground">Свободные темы доступны после входа</p>
          </div>
        )}
      </div>
    </section>
  );
}

/** Декоративный мотив героя — стопка «карточек» с галочками (чек-лист собеса), чистый CSS. */
function HeroMotif() {
  return (
    <div className="relative size-40 opacity-90">
      <div className="absolute right-4 top-4 h-24 w-36 rotate-6 rounded-xl border border-border/60 bg-card/80 shadow-sm" />
      <div className="absolute right-1 top-7 h-24 w-36 -rotate-3 rounded-xl border border-border/60 bg-card shadow-md" />
      <div className="absolute right-2 top-9 flex h-24 w-36 flex-col justify-center gap-2.5 rounded-xl border border-primary/30 bg-card px-4 shadow-lg">
        {[0, 1, 2].map((row) => (
          <div key={row} className="flex items-center gap-2">
            <span className="flex size-4 shrink-0 items-center justify-center rounded-full bg-primary/15">
              <Icons.check className="size-2.5 text-primary" />
            </span>
            <span
              className="h-1.5 rounded-full bg-border/70"
              style={{ width: `${70 - row * 14}%` }}
            />
          </div>
        ))}
      </div>
    </div>
  );
}

function HubSkeleton() {
  return (
    <div className="mx-auto w-full max-w-[1500px] space-y-6 p-4 md:p-6 xl:px-8">
      <Skeleton className="h-40 w-full rounded-2xl" />
      <div className="space-y-3">
        <Skeleton className="h-12 w-72 rounded-xl" />
        <Skeleton className="h-11 w-60 rounded-xl" />
      </div>
      <Skeleton className="h-10 w-full max-w-md" />
      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
        {Array.from({ length: 6 }).map((_, index) => (
          <Skeleton key={index} className="h-36 w-full rounded-xl" />
        ))}
      </div>
    </div>
  );
}
