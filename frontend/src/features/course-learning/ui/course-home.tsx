"use client";

import type { ReactNode } from "react";
import { courseCurriculumQueryOptions, type CourseCurriculumDto } from "@/entities/course";
import { courseLearningStateQueryOptions } from "@/entities/course-progress";
import { CourseSubscribeButton } from "@/entities/notification";
import { parseCourseViewTab } from "../lib/course-view-tabs";
import { cn } from "@/shared/lib/css";
import { routes } from "@/shared/config/routes";
import { useCourseSlug } from "@/shared/providers/course-id-provider";
import { CourseBreadcrumb } from "@/shared/ui/components/course-breadcrumb";
import { NotFoundFallback } from "@/shared/ui/components/not-found-fallback";
import { AuthorCredit, ContentImage, ShareButton } from "@/shared/ui/components";
import { Icons, type IconComponent } from "@/shared/ui/icons";
import { Badge } from "@/shared/ui/kit/badge";
import { Card } from "@/shared/ui/kit/card";
import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { CourseCurriculum } from "./course-curriculum";
import { CourseProgramPreview } from "./course-program-preview";
import { CurrentPositionCard } from "./current-position-card";
import { GettingStartedBlock } from "./getting-started-block";
import { CourseCollections } from "./course-collections";
import { SectionViewToggle, type ExtendedSectionViewMode } from "./section-view-toggle";
import { WhatsNewSection } from "./whats-new-section";
import { useResolvedCourseAccess } from "../model/use-resolved-course-access";
import { CourseAccessNotice } from "./course-access-notice";
import { selectProgramPreviewSections } from "../lib/program-preview";

export interface EnrollCardRenderProps {
  courseId: string;
  /** Автор курса — для композиции CTA «этот курс входит в планы» на page-уровне (#404). */
  authorId: string | undefined;
  hasFreeContent: boolean;
  state: "preview_paid" | "preview_trial" | "enrolled";
  startHref: string;
}

interface CourseHomeProps {
  courseId: string;
  /** Anonymous server snapshot used for the initial HTML only. */
  initialCourse?: CourseCurriculumDto | null;
  commentsSlot?: ReactNode;
  /**
   * Optional slot for the Telegram-chat CTA. Provided by the page-level container
   * (which can compose multiple features), so this feature stays FSD-compliant
   * (no cross-feature imports).
   */
  chatCtaSlot?: ReactNode;
  renderEnrollCard?: (props: EnrollCardRenderProps) => ReactNode;
}

export function CourseHome({
  courseId,
  initialCourse,
  commentsSlot,
  chatCtaSlot,
  renderEnrollCard,
}: CourseHomeProps) {
  const courseSlug = useCourseSlug();
  const router = useRouter();
  const searchParams = useSearchParams();
  const { data: course, isLoading: courseLoading } = useQuery({
    ...courseCurriculumQueryOptions(courseId),
    // The server fetch is anonymous. Mark it stale so hydration immediately
    // replaces it with the authenticated/enrolled curriculum when applicable.
    ...(initialCourse ? { initialData: initialCourse, initialDataUpdatedAt: 0 } : {}),
  });
  const access = useResolvedCourseAccess(courseId, course?.authorId);
  const { data: learningState, isLoading: learningStateLoading } = useQuery({
    ...courseLearningStateQueryOptions(courseId),
    enabled: access.isAuthenticated && !!courseId,
  });
  const hasActiveEnrollment = access.hasActiveEnrollment;
  const requestedTab = parseCourseViewTab(searchParams.get("tab"));
  const programView: ExtendedSectionViewMode = requestedTab === "projects" ? "projects" : "modules";

  const firstLearningItem = course?.sections
    .flatMap((section) => section.items)
    .find((item) => item.itemType === "Material" || item.itemType === "Issue");

  const firstLearningHref = firstLearningItem
    ? firstLearningItem.itemType === "Material"
      ? routes.courseMaterial(courseSlug, firstLearningItem.id)
      : routes.courseIssue(courseSlug, firstLearningItem.id)
    : routes.courseOverview(courseSlug);

  const hasFreeContent = course?.hasFreeContent ?? false;
  // Юзер с планом/полным доступом (или админ/автор) уже имеет доступ к курсу даже
  // без ленивой записи enrollment — показываем «Начать обучение», а НЕ ценник/«входит
  // в планы»/«Выбрать план» (#418). canAccessItem("ENROLLED") = level standard|admin.
  const hasCourseAccess = access.canAccessItem("ENROLLED");
  const enrollmentCardState =
    hasActiveEnrollment || hasCourseAccess
      ? "enrolled"
      : hasFreeContent
        ? "preview_trial"
        : "preview_paid";

  const moduleSections = course?.sections.filter((s) => s.itemType === "Module") ?? [];
  const projectSections = course?.sections.filter((s) => s.itemType !== "Module") ?? [];
  const visibleSections =
    programView === "modules" ? moduleSections : programView === "projects" ? projectSections : [];
  const projectCount = projectSections.length;
  const programPreviewSections =
    hasActiveEnrollment && !learningStateLoading
      ? selectProgramPreviewSections(moduleSections, learningState)
      : [];

  // Hero-статистика (для не-купивших). Интенсив/марафон — это «курс без заданий/
  // проектов» (Course.Kind), поэтому для них показываем компактно только число
  // уроков. Обычный курс — все ненулевые метрики (нулевые «Задачи»/«Проекты» прячем,
  // чтобы не маячили «0»). См. issue #291/#374 (Course.Kind) и UX-ревью каталога.
  const lessonsCount = moduleSections.reduce(
    (sum, s) => sum + s.items.filter((i) => i.itemType === "Material").length,
    0,
  );
  const issuesCount = moduleSections.reduce(
    (sum, s) => sum + s.items.filter((i) => i.itemType === "Issue").length,
    0,
  );
  // Тесты в витрине (#551): DISTINCT-квизы из модулей — один тест может быть размещён
  // в нескольких модулях; нулевое значение прячется общим фильтром ниже.
  const quizzesCount = new Set(
    moduleSections.flatMap((s) => s.items.filter((i) => i.itemType === "Quiz").map((i) => i.id)),
  ).size;
  const isCompactKind = course?.kind === "INTENSIVE" || course?.kind === "MARATHON";
  const heroStats = isCompactKind
    ? [{ icon: Icons.lesson, value: lessonsCount, label: "Уроки" }]
    : [
        { icon: Icons.module, value: moduleSections.length, label: "Модули" },
        { icon: Icons.lesson, value: lessonsCount, label: "Уроки" },
        { icon: Icons.issue, value: issuesCount, label: "Задачи" },
        { icon: Icons.quiz, value: quizzesCount, label: "Тесты" },
        { icon: Icons.project, value: projectCount, label: "Проекты" },
      ].filter((s) => s.value > 0);

  const gettingStartedSection = course?.gettingStartedModuleId
    ? (course.sections.find((s) => s.id === course.gettingStartedModuleId) ?? null)
    : null;

  if (courseLoading) {
    return (
      <div className="flex items-center justify-center h-full">
        <Icons.loading className="size-6 animate-spin text-muted-foreground" />
      </div>
    );
  }

  if (!course) {
    return (
      <NotFoundFallback message="Курс не найден" backHref={routes.home} backLabel="На главную" />
    );
  }

  return (
    <div className="h-full overflow-y-auto">
      <div className="flex shrink-0 items-center border-b border-border/60 px-3 py-2.5 md:px-6">
        <CourseBreadcrumb items={[{ label: course.title }]} />
      </div>

      <div className="mx-auto max-w-5xl px-4 py-6 md:px-8 pb-12">
        {/* Telegram chat CTA — visible only for enrolled users with bound chat.
            Slot pattern: the actual widget is provided by page-level container to keep
            this feature import-graph clean. */}
        {hasActiveEnrollment && chatCtaSlot ? <div className="mb-6">{chatCtaSlot}</div> : null}

        <CourseAccessNotice accessLevel={access.accessLevel} className="mb-6" />

        {/* Quick navigation blocks (enrolled only) */}
        {hasActiveEnrollment && learningState && (
          <div className="mb-10 grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-4">
            <QuickNavCard
              href={routes.courseProgram(courseSlug)}
              icon={Icons.listTree}
              label="Программа курса"
              value={`Пройдено ${learningState.summary.materialsViewed} из ${learningState.summary.materialsTotal}`}
              progress={
                learningState.summary.materialsTotal > 0
                  ? (learningState.summary.materialsViewed / learningState.summary.materialsTotal) *
                    100
                  : 0
              }
              color="teal"
              primary
            />
            <QuickNavCard
              href={routes.courseAssignments(courseSlug)}
              icon={Icons.issue}
              label="Задания"
              value={`${learningState.summary.issuesCompleted}/${learningState.summary.issuesTotal}`}
              progress={
                learningState.summary.issuesTotal > 0
                  ? (learningState.summary.issuesCompleted / learningState.summary.issuesTotal) *
                    100
                  : 0
              }
              color="orange"
            />
            <QuickNavCard
              href={routes.courseTests(courseSlug)}
              icon={Icons.quiz}
              label="Тесты"
              value={`${learningState.passedQuizIds?.length ?? 0}/${quizzesCount}`}
              progress={
                quizzesCount > 0
                  ? ((learningState.passedQuizIds?.length ?? 0) / quizzesCount) * 100
                  : 0
              }
              color="purple"
            />
            <QuickNavCard
              href={routes.courseKnowledgeBase(courseSlug)}
              icon={Icons.library}
              label="База знаний"
              value={`${learningState.summary.materialsTotal + learningState.summary.issuesTotal}`}
              color="blue"
            />
          </div>
        )}

        {/* Sequential layout (enrolled): Start here → Continue → Program → Collections → Feed */}
        {hasActiveEnrollment && (
          <div className="space-y-12">
            {gettingStartedSection && <GettingStartedBlock section={gettingStartedSection} />}

            <CurrentPositionCard
              curriculum={course}
              learningState={learningState}
              accessLevel={access.accessLevel}
              courseSlug={courseSlug}
            />

            <CourseProgramPreview
              sections={programPreviewSections}
              courseSlug={courseSlug}
              learningState={learningState}
              accessLevel={access.accessLevel}
              gettingStartedModuleId={course.gettingStartedModuleId}
              lastPositionItemId={learningState?.lastPosition?.entityId ?? null}
              currentPath={routes.courseOverview(courseSlug)}
            />

            <CourseCollections courseId={courseId} courseSlug={courseSlug} />

            <div className="pt-6 border-t border-border/50">
              <WhatsNewSection courseId={courseId} />
            </div>
          </div>
        )}

        {/* Enroll card + course info (not enrolled) */}
        {!learningStateLoading && !hasActiveEnrollment && (
          <>
            <Card className="group/hero relative overflow-hidden py-0 border-border/60 shadow-lg shadow-black/10">
              {/* Ambient blurred course cover backdrop */}
              {course.imageUrl && (
                <div className="absolute inset-0 pointer-events-none" aria-hidden="true">
                  <ContentImage
                    src={course.imageUrl}
                    alt=""
                    fill
                    sizes="100vw"
                    className="object-cover scale-110 opacity-30 blur-2xl"
                  />
                  <div className="absolute inset-0 bg-gradient-to-r from-card via-card/95 to-card/50" />
                  <div className="absolute inset-0 bg-gradient-to-b from-card/20 via-transparent to-card" />
                </div>
              )}

              <div className="relative grid gap-6 p-5 md:gap-8 md:p-8 xl:gap-10 xl:p-10 xl:grid-cols-[minmax(0,1.15fr)_minmax(0,1fr)]">
                {/* Left: course identity */}
                <div className="flex flex-col gap-5 min-w-0">
                  <div className="space-y-3">
                    <div className="flex items-center gap-3 min-w-0">
                      <h1 className="min-w-0 break-words text-3xl md:text-4xl lg:text-[2.75rem] font-bold tracking-tight leading-[1.05] text-foreground">
                        {course.title}
                      </h1>
                      {course.isNew && (
                        <Badge className="bg-emerald-500/90 text-white border-0 text-xs shrink-0">
                          New
                        </Badge>
                      )}
                      <CourseSubscribeButton
                        courseId={courseId}
                        variant="icon"
                        className="ml-auto shrink-0"
                      />
                      <ShareButton
                        url={routes.courseOverview(courseSlug)}
                        title={course.title}
                        withLabel={false}
                        variant="ghost"
                        size="icon"
                        className="shrink-0"
                      />
                    </div>
                    {course.description && (
                      <p className="text-sm md:text-base leading-relaxed text-muted-foreground max-w-xl break-words">
                        {course.description}
                      </p>
                    )}
                    {/* Автор курса (#569) — байлайн на hero публичной страницы; рендерит
                        null, если бэк не резолвнул автора. */}
                    <AuthorCredit
                      name={course.authorDisplayName}
                      avatarUrl={course.authorAvatarUrl ?? undefined}
                      size="md"
                    />
                  </div>
                </div>

                {/* Right: stats + enroll */}
                <div className="flex flex-col gap-5 min-w-0 xl:border-l xl:border-border/50 xl:pl-8">
                  {/* Stats grid — адаптивная: интенсив/марафон → только «Уроки»,
                      обычный курс → ненулевые метрики (см. heroStats выше). */}
                  {heroStats.length > 0 && (
                    <div
                      className={cn(
                        "grid gap-2",
                        heroStats.length === 1 ? "grid-cols-1" : "grid-cols-2",
                      )}
                    >
                      {heroStats.map((stat) => (
                        <StatPill
                          key={stat.label}
                          icon={stat.icon}
                          value={stat.value}
                          label={stat.label}
                        />
                      ))}
                    </div>
                  )}

                  {renderEnrollCard?.({
                    courseId,
                    authorId: course.authorId,
                    hasFreeContent,
                    state: enrollmentCardState,
                    startHref: firstLearningHref,
                  })}
                </div>
              </div>
            </Card>
          </>
        )}

        {/* Not enrolled: what the course gives — author landing copy (hides itself if empty) */}
        {!hasActiveEnrollment && (
          <CourseValueProps
            learningOutcomes={course.learningOutcomes}
            targetAudience={course.targetAudience}
            prerequisites={course.prerequisites}
          />
        )}

        {/* Not enrolled: show pinned collections as promo (component hides itself if empty) */}
        {!hasActiveEnrollment && (
          <div className="mt-8">
            <CourseCollections courseId={courseId} courseSlug={courseSlug} />
          </div>
        )}

        {/* Not enrolled: full curriculum for preview */}
        {!hasActiveEnrollment && course.sections.length > 0 && (
          <div className="mt-8">
            <div className="flex items-center justify-between mb-2.5">
              <p className="text-xs uppercase tracking-widest text-muted-foreground font-semibold">
                Программа курса
              </p>
              <SectionViewToggle
                value={programView}
                onChange={(nextView) =>
                  router.replace(
                    nextView === "projects"
                      ? `${routes.courseOverview(courseSlug)}?tab=projects`
                      : routes.courseOverview(courseSlug),
                    {
                      scroll: false,
                    },
                  )
                }
                className="w-64"
              />
            </div>
            <CourseCurriculum
              sections={visibleSections}
              accessLevel={access.accessLevel}
              learningState={learningState}
              activeTab={programView}
              gettingStartedModuleId={course.gettingStartedModuleId}
              courseTitle={course.title}
            />
            {visibleSections.length === 0 && (
              <p className="text-sm text-muted-foreground text-center py-6">
                {programView === "modules" ? "Нет модулей" : "Нет проектов"}
              </p>
            )}
          </div>
        )}

        {/* Not enrolled: course-level materials feed — shows gated items with lock icons */}
        {!hasActiveEnrollment && (
          <div className="mt-12 pt-6 border-t border-border/50">
            <WhatsNewSection courseId={courseId} />
          </div>
        )}

        {commentsSlot}

        <div className="h-4" />
      </div>
    </div>
  );
}

/**
 * «Что даёт курс» — author landing copy для не-купивших: чему научатся, для кого
 * курс, что нужно знать заранее. Скрывается целиком, если автор ничего не заполнил.
 * «Чему вы научитесь» — featured-карточка (2 колонки), остальные две — компактные.
 */
function CourseValueProps({
  learningOutcomes,
  targetAudience,
  prerequisites,
}: {
  learningOutcomes?: string[];
  targetAudience?: string[];
  prerequisites?: string[];
}) {
  const outcomes = learningOutcomes ?? [];
  const audience = targetAudience ?? [];
  const prereqs = prerequisites ?? [];

  if (outcomes.length === 0 && audience.length === 0 && prereqs.length === 0) {
    return null;
  }

  return (
    <div className="mt-8 space-y-4">
      {outcomes.length > 0 && (
        <Card className="border-primary/25 bg-gradient-to-br from-primary/[0.07] via-card to-card p-6 md:p-8">
          <div className="mb-5 flex items-center gap-2.5">
            <span className="flex size-9 items-center justify-center rounded-lg bg-primary/15 text-primary">
              <Icons.graduation size={18} />
            </span>
            <h2 className="text-xl font-bold tracking-tight">Чему вы научитесь</h2>
          </div>
          <ul className="grid gap-x-6 gap-y-3 sm:grid-cols-2">
            {outcomes.map((item, index) => (
              <li key={`${index}-${item}`} className="flex items-start gap-2.5">
                <Icons.check className="mt-0.5 size-4 shrink-0 text-primary" />
                <span className="text-sm leading-relaxed text-foreground/90">{item}</span>
              </li>
            ))}
          </ul>
        </Card>
      )}

      {(audience.length > 0 || prereqs.length > 0) && (
        <div className="grid gap-4 md:grid-cols-2">
          {audience.length > 0 && (
            <ValuePropCard title="Для кого этот курс" icon={Icons.target} items={audience} />
          )}
          {prereqs.length > 0 && (
            <ValuePropCard
              title="Что нужно знать заранее"
              icon={Icons.listChecks}
              items={prereqs}
            />
          )}
        </div>
      )}
    </div>
  );
}

function ValuePropCard({
  title,
  icon: Icon,
  items,
}: {
  title: string;
  icon: IconComponent;
  items: string[];
}) {
  return (
    <Card className="h-full border-border/60 p-6">
      <div className="mb-4 flex items-center gap-2.5">
        <span className="flex size-8 items-center justify-center rounded-lg bg-muted text-muted-foreground">
          <Icon size={16} />
        </span>
        <h3 className="font-semibold">{title}</h3>
      </div>
      <ul className="space-y-2.5">
        {items.map((item, index) => (
          <li key={`${index}-${item}`} className="flex items-start gap-2.5">
            <Icons.check className="mt-0.5 size-4 shrink-0 text-primary/70" />
            <span className="text-sm leading-relaxed text-muted-foreground">{item}</span>
          </li>
        ))}
      </ul>
    </Card>
  );
}

const navColorMap = {
  teal: { bg: "bg-teal-dim", border: "border-teal/25", text: "text-teal", bar: "bg-teal" },
  orange: { bg: "bg-orange/10", border: "border-orange/25", text: "text-orange", bar: "bg-orange" },
  blue: { bg: "bg-blue/10", border: "border-blue/25", text: "text-blue", bar: "bg-blue" },
  purple: {
    bg: "bg-purple-500/10",
    border: "border-purple-500/25",
    text: "text-purple-400",
    bar: "bg-purple-500",
  },
} as const;

function QuickNavCard({
  href,
  icon: Icon,
  label,
  value,
  progress,
  color = "teal",
  primary = false,
}: {
  href: string;
  icon: IconComponent;
  label: string;
  value?: string | number;
  progress?: number;
  color?: keyof typeof navColorMap;
  primary?: boolean;
}) {
  const c = navColorMap[color];
  return (
    <Link href={href}>
      <Card
        className={cn(
          "group relative h-full py-3.5 px-4 gap-0 overflow-hidden transition-all duration-200",
          primary
            ? "border-teal/50 bg-teal-dim/40 ring-1 ring-teal/25 hover:border-teal/70 hover:bg-teal-dim/60 hover:ring-teal/40"
            : "border-border/60 hover:border-primary/30",
        )}
      >
        {/* Progress bar at bottom */}
        {progress != null && progress > 0 && (
          <div className="absolute bottom-0 left-0 right-0 h-1 bg-secondary/50">
            <div
              className={cn("h-full rounded-r-full transition-[width] duration-700", c.bar)}
              style={{ width: `${Math.min(progress, 100)}%`, opacity: 0.6 }}
            />
          </div>
        )}

        <div className="flex items-start gap-3">
          <div
            className={cn(
              "size-9 rounded-lg flex items-center justify-center shrink-0 border",
              c.bg,
              c.border,
            )}
          >
            <Icon size={16} className={c.text} />
          </div>
          <div className="min-w-0 flex-1">
            <p className="text-sm font-semibold text-foreground group-hover:text-primary transition-colors duration-200">
              {label}
            </p>
            {value != null && (
              <p className="text-xs text-muted-foreground tabular-nums mt-0.5">{value}</p>
            )}
          </div>
          <Icons.arrowRight
            size={14}
            className="hidden sm:block text-muted-foreground/30 group-hover:text-primary group-hover:translate-x-0.5 transition-all duration-300 shrink-0"
          />
        </div>
      </Card>
    </Link>
  );
}

function StatPill({
  icon: Icon,
  value,
  label,
}: {
  icon: IconComponent;
  value: number | string;
  label: string;
}) {
  return (
    <div className="flex items-center gap-2.5 rounded-xl bg-background/50 backdrop-blur-sm border border-border/60 px-3 py-2.5">
      <div className="size-8 rounded-lg bg-primary/10 border border-primary/20 flex items-center justify-center shrink-0">
        <Icon size={14} className="text-primary" />
      </div>
      <div className="min-w-0">
        <div className="text-sm font-semibold tabular-nums leading-tight text-foreground">
          {value}
        </div>
        <div className="text-[10px] uppercase tracking-[0.15em] text-muted-foreground mt-0.5">
          {label}
        </div>
      </div>
    </div>
  );
}
