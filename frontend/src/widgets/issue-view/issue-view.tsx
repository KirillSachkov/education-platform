"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { Loader2 } from "lucide-react";
import {
  courseCurriculumQueryOptions,
  getAdjacentIssues,
  getAdjacentLearningItems,
  getCourseItemHref,
} from "@/entities/course";
import {
  courseLearningStateQueryOptions,
  courseProgressQueryOptions,
  issueSubmissionHistoryQueryOptions,
  type IssueProgressStatus,
} from "@/entities/course-progress";
import { issueDetailQueryOptions } from "@/entities/issue";
import { projectDetailQueryOptions } from "@/entities/project";
import { isGitHubPullRequestUrl } from "@/entities/review-submission";
import {
  useStartIssue,
  useSubmitIssue,
  parseCourseViewTab,
  useResolvedCourseAccess,
} from "@/features/course-learning";
import { ReviewConnectionGate, useReviewAppConnection } from "@/features/connect-review-app";
import { useMyProfile } from "@/features/profile-manage";
import { CommentSection } from "@/features/comments";
import { isContentAccessError, isForbiddenError } from "@/shared/api";
import {
  resolveSecondaryUnlockHref,
  resolveUnlockHref,
  type LockReason,
} from "@/shared/lib/lock-copy";
import { LockCallout } from "@/shared/ui/components/lock-callout";
import { EntityTypes } from "@/shared/config/entity-types";
import { routes } from "@/shared/config/routes";
import { useCourseSlug } from "@/shared/providers/course-id-provider";
import { CourseBreadcrumb } from "@/shared/ui/components/course-breadcrumb";
import { LessonNav } from "@/shared/ui/components/lesson-nav";
import { MarkdownContent } from "@/shared/ui/components/markdown-content";
import { NotFoundFallback } from "@/shared/ui/components/not-found-fallback";
import { BookmarkToggleButton } from "@/entities/bookmark";
import { Icons } from "@/shared/ui/icons";
import { Skeleton } from "@/shared/ui/kit/skeleton";
import { AiReviewBlock } from "@/widgets/ai-review-block";
import { IssueHeader } from "./ui/issue-header";
import { IssueMaterials } from "./ui/issue-materials";
import { IssueSubmissionProgress } from "./ui/issue-submission-progress";
import { IssueSubmissionForm } from "./ui/issue-submission-form";
import { RequestAuthorHelpButton } from "./ui/request-author-help-button";
import { AskAuthorQuestionButton } from "./ui/ask-author-question-button";

interface IssueViewProps {
  courseId: string;
  issueId: string;
}

export function IssueView({ courseId, issueId }: IssueViewProps) {
  const courseSlug = useCourseSlug();
  const queryClient = useQueryClient();
  const searchParams = useSearchParams();
  const [submissionText, setSubmissionText] = useState("");

  const { data: curriculum, isLoading: isCurriculumLoading } = useQuery(
    courseCurriculumQueryOptions(courseId),
  );
  const access = useResolvedCourseAccess(courseId, curriculum?.authorId);
  const { data: learningState, isLoading: isLearningStateLoading } = useQuery({
    ...courseLearningStateQueryOptions(courseId),
    enabled: access.isAuthenticated && !!courseId,
  });
  const hasActiveEnrollment = access.hasActiveEnrollment;
  const {
    data: issue,
    isLoading: isIssueLoading,
    error: issueError,
  } = useQuery(issueDetailQueryOptions(issueId));
  const { data: project } = useQuery({
    ...projectDetailQueryOptions(issue?.projectId ?? ""),
    enabled: access.isAuthenticated && !!issue?.projectId,
  });
  const { data: issueHistory, isLoading: isIssueHistoryLoading } = useQuery({
    ...issueSubmissionHistoryQueryOptions(courseId, issueId),
    enabled: !!issueId && hasActiveEnrollment,
    refetchOnWindowFocus: true,
    refetchInterval: (query) => {
      const currentStatus = (query.state.data as { currentStatus?: string } | undefined)
        ?.currentStatus;
      return currentStatus === "UNDER_REVIEW" ? 10000 : false;
    },
  });

  const sectionId = searchParams.get("section");
  const tab = parseCourseViewTab(searchParams.get("tab"), "modules");
  const navigation = getAdjacentLearningItems(curriculum, issueId, sectionId);

  const issueProgress = learningState?.issues.find((item) => item.issueId === issueId);
  const issueStatus: IssueProgressStatus =
    issueHistory?.currentStatus ?? issueProgress?.status ?? "NOT_STARTED";
  const currentProjectItem = project?.items.find((item) => item.issueId === issueId);
  const canStartIssue = hasActiveEnrollment && issueStatus === "NOT_STARTED";
  const isSelfCheck = issue?.submissionMode === "SELF_CHECK";
  const requiresGithubConnection = issue?.requiresGithubConnection ?? true;
  const requiresReviewApp = issue?.requiresReviewApp ?? true;
  const isAiFlowEnabled = !isSelfCheck && (issue?.isAutoReviewEnabled ?? true);

  useEffect(() => {
    if (!issueHistory?.currentStatus || !issueProgress?.status) {
      return;
    }

    if (issueHistory.currentStatus !== issueProgress.status) {
      void queryClient.invalidateQueries({
        queryKey: [courseProgressQueryOptions.baseKey, courseId],
      });
    }
  }, [courseId, issueHistory?.currentStatus, issueProgress?.status, queryClient]);

  const startIssueMutation = useStartIssue(courseId, issueId);

  const submitIssueMutation = useSubmitIssue(courseId, issueId);

  // Gate сдачи PR: пока студент не привязал GitHub и не установил review-app,
  // на submit-экране показываем блок подключения вместо поля ввода ссылки на PR.
  const isSubmitState = issueStatus === "IN_PROGRESS" || issueStatus === "REQUESTED_CHANGES";
  const needsConnectionForSubmit =
    hasActiveEnrollment &&
    isSubmitState &&
    !isSelfCheck &&
    (requiresGithubConnection || requiresReviewApp);
  const { profile: myProfile, isPending: isProfileLoading } = useMyProfile();
  const { hasActiveInstallation, isLoading: isInstallationLoading } = useReviewAppConnection({
    enabled: needsConnectionForSubmit && requiresReviewApp,
  });
  const isGithubReviewConnected =
    (!requiresGithubConnection || !!myProfile?.hasGitHubLinked) &&
    (!requiresReviewApp || hasActiveInstallation);

  if (
    isCurriculumLoading ||
    isLearningStateLoading ||
    isIssueLoading ||
    (hasActiveEnrollment && isIssueHistoryLoading)
  ) {
    return (
      <div className="flex items-center justify-center h-full">
        <Loader2 className="size-6 animate-spin text-muted-foreground" />
      </div>
    );
  }

  if (issueError && (isForbiddenError(issueError) || isContentAccessError(issueError))) {
    const lockReason: LockReason =
      access.accessLevel === "anonymous" ? "anonymous" : "not_enrolled";
    return (
      <IssueAccessLocked
        lockReason={lockReason}
        courseSlug={courseSlug}
        courseTitle={curriculum?.title}
      />
    );
  }

  if (!issue || !curriculum || !navigation.currentItem) {
    return (
      <NotFoundFallback
        message="Задача не найдена"
        backHref={routes.courseOverview(courseSlug)}
        backLabel="К курсу"
      />
    );
  }

  if (!issue.isAccessible) {
    // Бэкенд вернул DTO, но ресурс закрыт. Единый lock-callout — без toast+redirect.
    return (
      <IssueAccessLocked
        lockReason="not_enrolled"
        courseSlug={courseSlug}
        courseTitle={curriculum?.title}
      />
    );
  }

  const issuesOnlyNav = getAdjacentIssues(curriculum, issueId);

  const programPrev = navigation.previousItem
    ? {
        id: navigation.previousItem.id,
        title: navigation.previousItem.title,
        href: getCourseItemHref(
          courseSlug,
          navigation.previousItem.itemType,
          navigation.previousItem.id,
          { tab },
        ),
      }
    : null;

  const programNext = navigation.nextItem
    ? {
        id: navigation.nextItem.id,
        title: navigation.nextItem.title,
        href: getCourseItemHref(courseSlug, navigation.nextItem.itemType, navigation.nextItem.id, {
          tab,
        }),
      }
    : null;

  const issuesPrev = issuesOnlyNav.previousItem
    ? {
        id: issuesOnlyNav.previousItem.id,
        title: issuesOnlyNav.previousItem.title,
        href: getCourseItemHref(courseSlug, "Issue", issuesOnlyNav.previousItem.id, { tab }),
      }
    : null;

  const issuesNext = issuesOnlyNav.nextItem
    ? {
        id: issuesOnlyNav.nextItem.id,
        title: issuesOnlyNav.nextItem.title,
        href: getCourseItemHref(courseSlug, "Issue", issuesOnlyNav.nextItem.id, { tab }),
      }
    : null;

  // Hide the "issues-only" row when it's identical to the program row — i.e.
  // the previous/next item is already the same issue in both lists. Avoids
  // duplicating buttons when the current task lives in a project-only section
  // with no surrounding materials.
  const isProgramSameAsIssues =
    programPrev?.id === issuesPrev?.id && programNext?.id === issuesNext?.id;

  const sectionType = navigation.currentItem.sectionType;
  const currentSectionId = navigation.currentItem.sectionId;
  const sectionHref =
    currentSectionId && sectionType === "Module"
      ? routes.courseModule(courseSlug, currentSectionId)
      : currentSectionId && sectionType !== "Module"
        ? routes.courseProject(courseSlug, currentSectionId)
        : routes.courseAssignments(courseSlug);
  const breadcrumbs = [
    { label: curriculum.title, href: routes.courseOverview(courseSlug) },
    ...(navigation.currentItem.sectionTitle
      ? [{ label: navigation.currentItem.sectionTitle, href: sectionHref }]
      : []),
    { label: issue.title },
  ];

  const submissionConnectionGate =
    needsConnectionForSubmit && !isGithubReviewConnected ? (
      isProfileLoading || isInstallationLoading ? (
        <Skeleton className="h-28 w-full rounded-xl" />
      ) : (
        <ReviewConnectionGate
          hasGitHubLinked={!!myProfile?.hasGitHubLinked}
          requiresGithubConnection={requiresGithubConnection}
          requiresReviewApp={requiresReviewApp}
        />
      )
    ) : null;

  return (
    <div className="flex flex-col h-full overflow-hidden">
      <div className="flex items-center px-3 md:px-6 py-2.5 border-b shrink-0 min-w-0 overflow-x-auto">
        <CourseBreadcrumb items={breadcrumbs} />
      </div>

      <div className="flex-1 overflow-y-auto">
        <div className="max-w-3xl mx-auto px-4 md:px-6 py-6 md:py-8">
          {/* Title + metadata */}
          <IssueHeader
            title={issue.title}
            projectTitle={project?.title ?? navigation.currentItem.sectionTitle}
            status={issueStatus}
            maxScore={currentProjectItem?.maxScore ?? null}
            action={
              <BookmarkToggleButton courseId={courseId} entityType="Issue" entityId={issueId} />
            }
          />

          {/* Action + AI review — one unified flow right after header */}
          <div className="mb-6 space-y-4">
            <IssueSubmissionForm
              status={issueStatus}
              submissionMode={issue.submissionMode}
              submissionText={submissionText}
              onSubmissionTextChange={setSubmissionText}
              onSubmit={() =>
                submitIssueMutation.mutate(
                  issue.submissionMode === "SELF_CHECK"
                    ? { contentPayload: submissionText.trim() }
                    : { submissionUrl: submissionText.trim() },
                  { onSuccess: () => setSubmissionText("") },
                )
              }
              onStart={() => {
                if (!issue) return;
                startIssueMutation.mutate({ projectId: issue.projectId });
              }}
              canStart={canStartIssue}
              canSubmit={
                // PR-режим требует ссылку на конкретный pull request (#718); self-check — любой текст.
                isSelfCheck
                  ? submissionText.trim().length > 0
                  : isGitHubPullRequestUrl(submissionText.trim())
              }
              isSubmitting={submitIssueMutation.isPending}
              isStarting={startIssueMutation.isPending}
              disabled={!hasActiveEnrollment}
              helperText={hasActiveEnrollment ? null : "Действие доступно после записи на курс."}
              selfCheckInstructions={issue.selfCheckInstructions}
              feedback={
                issueHistory?.attempts[issueHistory.attempts.length - 1]?.feedback ??
                issueProgress?.latestSubmission?.feedback ??
                null
              }
              connectionGate={submissionConnectionGate}
            />

            {/* AI review block — co-located with the submit form so the
                «Ожидает проверки» status and the AI verdict live in one flow (#15).
                Widget сам решает «нет AiReview» / «GitHub App не подключён» / etc. */}
            {isAiFlowEnabled && issueHistory && issueHistory.attempts.length > 0 && (
              <AiReviewBlock
                submissionId={issueHistory.attempts[issueHistory.attempts.length - 1].submissionId}
                isOpen={
                  issueHistory.attempts[issueHistory.attempts.length - 1].reviewStatus ===
                    "PENDING" ||
                  issueHistory.attempts[issueHistory.attempts.length - 1].reviewStatus ===
                    "IN_REVIEW"
                }
                isCompleted={issueStatus === "COMPLETED"}
              />
            )}

            {/* #383 «Позвать автора» — пока решение ждёт проверки, студент может явно
                подключить автора (AI-ассистент не справился / нужна живая помощь). */}
            {issueHistory &&
              isAiFlowEnabled &&
              issueHistory.attempts.length > 0 &&
              (() => {
                const latest = issueHistory.attempts[issueHistory.attempts.length - 1];
                const awaiting =
                  latest.reviewStatus === "PENDING" || latest.reviewStatus === "IN_REVIEW";
                if (!awaiting) return null;
                return (
                  <div className="rounded-xl border border-border/60 bg-muted/20 px-4 py-3">
                    <p className="mb-2 text-sm text-muted-foreground">
                      Не получается с AI-проверкой? Позовите автора — он подключится и поможет
                      разобраться.
                    </p>
                    <RequestAuthorHelpButton
                      submissionId={latest.submissionId}
                      alreadyRequestedAt={latest.authorHelpRequestedAt}
                    />
                  </div>
                );
              })()}
          </div>

          {/* Attached materials (shown before description so student sees references first) */}
          <IssueMaterials
            issueId={issueId}
            internalMaterials={issue.internalMaterials}
            externalLinks={issue.externalLinks}
          />

          {/* Description */}
          {issue.content && (
            <div className="mb-6">
              <div className="flex items-center gap-3 mb-4 pt-4 border-t">
                <h2 className="text-base font-semibold">Задание</h2>
              </div>
              <div className="border-l-2 border-primary/30 pl-4">
                <MarkdownContent variant="compact" className="min-w-0 overflow-x-auto break-words">
                  {issue.content}
                </MarkdownContent>
              </div>
            </div>
          )}

          {/* #693 «Задать вопрос автору» — доступно ДО сабмишена: студент, зависший на
              чтении задания, явно зовёт автора, не дожидаясь отправки решения. */}
          {access.isAuthenticated && (
            <div className="mb-6 flex flex-wrap items-center gap-2">
              <AskAuthorQuestionButton issueId={issueId} enabled={access.isAuthenticated} />
            </div>
          )}

          {/* Submission history (collapsible) */}
          {issueHistory && issueHistory.attempts.length > 0 && (
            <details className="mt-6 rounded-xl border border-border/60 overflow-hidden">
              <summary className="px-4 py-3 text-sm font-medium text-muted-foreground cursor-pointer hover:bg-accent/30 transition-colors">
                История выполнения ({issueHistory.attempts.length})
              </summary>
              <div className="border-t border-border/60">
                <IssueSubmissionProgress history={issueHistory} />
              </div>
            </details>
          )}

          {/* Navigation — program order as the primary cards, issues-only as a
              compact secondary line so the two streams don't look identical. */}
          <LessonNav
            prev={programPrev}
            next={programNext}
            tasks={{
              prev: isProgramSameAsIssues ? null : issuesPrev,
              next: isProgramSameAsIssues ? null : issuesNext,
            }}
          />

          <CommentSection
            targetType={EntityTypes.ISSUE}
            targetId={issueId}
            className="mt-8 border-t border-border/60 pt-8"
          />

          <div className="h-12" />
        </div>
      </div>
    </div>
  );
}

/**
 * Inline lock-callout для задач — триггерится и на 401/403, и когда DTO пришёл
 * с `isAccessible=false`. Юзер остаётся на той же странице, видит шапку
 * с back-кнопкой и LockCallout по центру. Браузерный «назад» возвращает
 * туда, откуда пришёл.
 */
function IssueAccessLocked({
  lockReason,
  courseSlug,
  courseTitle,
}: {
  lockReason: LockReason;
  courseSlug: string;
  courseTitle?: string | null;
}) {
  const returnTo = typeof window !== "undefined" ? window.location.pathname : null;
  const ctaHref = resolveUnlockHref({ lockReason, returnTo });
  const secondaryHref = resolveSecondaryUnlockHref({ lockReason });
  const backHref = routes.courseOverview(courseSlug);
  return (
    <div className="flex h-full flex-col overflow-hidden">
      <div className="flex items-center gap-2 border-b px-3 py-2.5 md:px-6">
        <Link
          href={backHref}
          className="inline-flex items-center gap-1 text-xs text-muted-foreground transition-colors hover:text-foreground"
        >
          <Icons.chevronLeft className="size-3.5" />К курсу{courseTitle ? ` «${courseTitle}»` : ""}
        </Link>
      </div>
      <div className="flex-1 overflow-y-auto">
        <div className="mx-auto flex max-w-md flex-col items-stretch px-4 py-12 sm:py-16">
          <div className="rounded-2xl border border-border/60 bg-card/95 p-5 shadow-xl shadow-black/20">
            <LockCallout
              reason={lockReason}
              courseTitle={courseTitle ?? null}
              ctaHref={ctaHref}
              secondaryCtaHref={secondaryHref}
            />
          </div>
        </div>
      </div>
    </div>
  );
}
