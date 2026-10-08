"use client";

import { isGitHubPullRequestUrl } from "@/entities/review-submission";
import type { ReviewAiVerdict, ReviewSubmissionItemDto } from "@/entities/review-submission";
import { cn } from "@/shared/lib/css";
import { Icons } from "@/shared/ui/icons";
import { UserAvatar } from "@/shared/ui/components";
import { StatusBadge } from "@/shared/ui/components/status-badge";
import { Badge } from "@/shared/ui/kit/badge";
import { Button } from "@/shared/ui/kit/button";
import { Card } from "@/shared/ui/kit/card";
import { AiReviewHistory } from "./ai-review-history";
import { AttemptHistory } from "./attempt-history";

const AI_VERDICT_LABEL: Record<ReviewAiVerdict, string> = {
  LOOKS_GOOD: "AI: принято",
  MINOR_ISSUES: "AI: мелкие замечания",
  MAJOR_ISSUES: "AI: серьёзные замечания",
  OFF_TOPIC: "AI: не по теме",
};

const AI_VERDICT_TONE: Record<ReviewAiVerdict, string> = {
  LOOKS_GOOD: "border-green/30 bg-green/10 text-green",
  MINOR_ISSUES: "border-yellow/30 bg-yellow/10 text-yellow",
  MAJOR_ISSUES: "border-red/30 bg-red/10 text-red",
  OFF_TOPIC: "border-muted-foreground/30 bg-muted/40 text-muted-foreground",
};

const ISSUE_PROGRESS_LABEL: Record<ReviewSubmissionItemDto["issueProgressStatus"], string> = {
  NOT_STARTED: "Не начато",
  IN_PROGRESS: "В работе",
  UNDER_REVIEW: "На проверке",
  REQUESTED_CHANGES: "Нужны правки",
  COMPLETED: "Выполнено",
};

type AiReviewStatus = NonNullable<ReviewSubmissionItemDto["aiReviewStatus"]>;

const AI_STATUS_LABEL: Record<AiReviewStatus, string> = {
  QUEUED: "AI в очереди",
  RUNNING: "AI проверяет…",
  READY: "AI: проверено",
  FAILED: "AI: ошибка",
};

const AI_STATUS_TONE: Record<AiReviewStatus, string> = {
  QUEUED: "border-blue/30 bg-blue/10 text-blue",
  RUNNING: "border-blue/30 bg-blue/10 text-blue",
  READY: "border-green/30 bg-green/10 text-green",
  FAILED: "border-red/30 bg-red/10 text-red",
};

function getShortId(value: string | null | undefined, length = 8) {
  if (!value) return "—";
  return `${value.slice(0, length)}...`;
}

function formatDate(value: string | null) {
  if (!value) return null;

  return new Date(value).toLocaleDateString("ru-RU", {
    day: "numeric",
    month: "long",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  });
}

interface ReviewCardProps {
  submission: ReviewSubmissionItemDto;
  onStartReview: (submission: ReviewSubmissionItemDto) => Promise<void>;
  onApprove: (submission: ReviewSubmissionItemDto) => void;
  onRequestChanges: (submission: ReviewSubmissionItemDto) => void;
  onReopen: (submission: ReviewSubmissionItemDto) => Promise<void>;
  onCancelReview: (submission: ReviewSubmissionItemDto) => Promise<void>;
  onMarkComplete: (submission: ReviewSubmissionItemDto) => void;
  onOpenStatusOverride: (submission: ReviewSubmissionItemDto) => void;
  isStartPending: boolean;
  isApprovePending: boolean;
  isRequestChangesPending: boolean;
  isReopenPending: boolean;
  isCancelReviewPending: boolean;
  isMarkCompletePending: boolean;
  isSetStatusPending: boolean;
}

export function ReviewCard({
  submission,
  onStartReview,
  onApprove,
  onRequestChanges,
  onReopen,
  onCancelReview,
  onMarkComplete,
  onOpenStatusOverride,
  isStartPending,
  isApprovePending,
  isRequestChangesPending,
  isReopenPending,
  isCancelReviewPending,
  isMarkCompletePending,
  isSetStatusPending,
}: ReviewCardProps) {
  const isInReview = submission.reviewStatus === "IN_REVIEW";
  const canStartReview = submission.reviewStatus === "PENDING";
  const canReview = isInReview;
  const isReviewed =
    submission.reviewStatus === "APPROVED" || submission.reviewStatus === "CHANGES_REQUESTED";
  // #383 — «Отметить выполненным»: force-approve из любого не-принятого статуса
  // (PENDING / IN_REVIEW / CHANGES_REQUESTED). Автор — последнее слово над AI.
  const canMarkComplete = submission.reviewStatus !== "APPROVED";
  const isActionDisabled =
    isStartPending ||
    isApprovePending ||
    isRequestChangesPending ||
    isReopenPending ||
    isCancelReviewPending ||
    isMarkCompletePending ||
    isSetStatusPending;

  const issueLabel = submission.issueTitle ?? `Задача ${getShortId(submission.issueId)}`;
  const courseLabel = submission.courseTitle ?? `Курс ${getShortId(submission.courseId)}`;
  const projectLabel = submission.projectTitle ?? `Проект ${getShortId(submission.projectId)}`;
  const hasMultipleAttempts = submission.attemptsCount > 1;
  const studentTgHandle = submission.studentTelegramUsername?.replace(/^@+/, "") || null;
  const canUseAiReview = isGitHubPullRequestUrl(submission.payload);

  return (
    <Card className={cn("p-0 gap-0 overflow-hidden", hasMultipleAttempts && "border-yellow/40")}>
      {hasMultipleAttempts && (
        <div className="flex items-center gap-2 px-4 md:px-5 py-2.5 bg-yellow/10 border-b border-yellow/30 text-sm">
          <Icons.warning size={16} className="text-yellow shrink-0" />
          <span className="font-semibold text-yellow">Попыток: {submission.attemptsCount}</span>
          <span className="text-muted-foreground hidden sm:inline">
            — показана последняя (#{submission.submissionNo}), история ниже
          </span>
        </div>
      )}
      <div className="flex flex-col sm:flex-row items-start gap-4 p-4 md:p-5">
        <UserAvatar
          name={submission.studentName ?? submission.studentUsername}
          avatarId={submission.studentAvatarId}
          userId={submission.studentId}
          className="size-10 shrink-0 hidden sm:block"
        />

        <div className="flex-1 min-w-0">
          <div className="flex items-center gap-2 mb-1.5 flex-wrap">
            <span className="text-sm font-semibold">
              {submission.studentName ??
                submission.studentUsername ??
                `Студент ${getShortId(submission.studentId)}`}
            </span>
            <StatusBadge status={submission.reviewStatus} />
            <StatusBadge
              status={submission.issueProgressStatus}
              label={`Задание: ${ISSUE_PROGRESS_LABEL[submission.issueProgressStatus]}`}
              className="text-xs"
            />
            {!hasMultipleAttempts && (
              <Badge variant="secondary" className="text-xs">
                Попытка #{submission.submissionNo}
              </Badge>
            )}
            {submission.latestAiVerdict ? (
              <Badge
                variant="outline"
                className={cn("text-xs font-medium", AI_VERDICT_TONE[submission.latestAiVerdict])}
                data-testid="ai-verdict-badge"
                title={
                  submission.aiIterationsCount > 0
                    ? `${submission.aiIterationsCount} AI-итерация(й)`
                    : undefined
                }
              >
                {AI_VERDICT_LABEL[submission.latestAiVerdict]}
              </Badge>
            ) : submission.aiReviewStatus ? (
              <Badge
                variant="outline"
                className={cn(
                  "text-xs font-medium gap-1",
                  AI_STATUS_TONE[submission.aiReviewStatus],
                )}
                data-testid="ai-status-badge"
              >
                {submission.aiReviewStatus === "QUEUED" ||
                submission.aiReviewStatus === "RUNNING" ? (
                  <Icons.loading size={11} className="animate-spin" />
                ) : null}
                {AI_STATUS_LABEL[submission.aiReviewStatus]}
              </Badge>
            ) : (
              // #718 — ни вердикта, ни статуса → AI ничего не проверяла (например, студент
              // сдал не-PR ссылку до серверной валидации). Нейтральный чип вместо пустоты,
              // чтобы автор не принимал «ничего» за «на проверке».
              <Badge
                variant="outline"
                className="text-xs font-medium gap-1 border-muted-foreground/30 bg-muted/40 text-muted-foreground"
                data-testid="ai-notstarted-badge"
                title="AI-проверка по этой сдаче не запускалась"
              >
                <Icons.pending size={11} /> AI-проверка не запущена
              </Badge>
            )}
            {submission.authorHelpRequestedAt ? (
              <Badge
                variant="outline"
                className="text-xs font-medium gap-1 border-yellow/40 bg-yellow/10 text-yellow"
                data-testid="author-help-badge"
                title={`Студент позвал автора ${formatDate(submission.authorHelpRequestedAt) ?? ""}`}
              >
                <Icons.warning size={11} /> Нужна помощь автора
              </Badge>
            ) : null}
            {submission.studentQuestionAt ? (
              <Badge
                variant="outline"
                className="text-xs font-medium gap-1 border-blue/40 bg-blue/10 text-blue"
                data-testid="student-question-badge"
                title={`Вопрос от студента ${formatDate(submission.studentQuestionAt) ?? ""}`}
              >
                <Icons.help size={11} /> Новый вопрос от студента
              </Badge>
            ) : null}
          </div>

          <div className="flex items-start gap-1.5 mb-1">
            <Icons.issue size={16} className="mt-0.5 shrink-0 text-muted-foreground" />
            <p className="text-sm font-medium leading-snug truncate" title={issueLabel}>
              {issueLabel}
            </p>
          </div>

          <div className="flex items-center gap-x-3 gap-y-1 text-xs text-muted-foreground flex-wrap mb-2">
            <span className="inline-flex items-center gap-1 min-w-0">
              <Icons.course size={13} className="shrink-0" />
              <span className="truncate" title={courseLabel}>
                {courseLabel}
              </span>
            </span>
            <span className="inline-flex items-center gap-1 min-w-0">
              <Icons.project size={13} className="shrink-0" />
              <span className="truncate" title={projectLabel}>
                {projectLabel}
              </span>
            </span>
          </div>

          {(submission.studentTelegramUsername || submission.studentEmail) && (
            <div
              className="flex flex-wrap items-center gap-x-3 gap-y-1 text-xs mb-2"
              data-testid="student-contacts"
            >
              {studentTgHandle && (
                <a
                  href={`https://t.me/${studentTgHandle}`}
                  target="_blank"
                  rel="noopener noreferrer"
                  className="inline-flex items-center gap-1 text-teal hover:underline"
                  title="Написать студенту в Telegram"
                >
                  <Icons.telegram size={13} className="shrink-0" />@{studentTgHandle}
                </a>
              )}
              {submission.studentEmail && (
                <a
                  href={`mailto:${submission.studentEmail}`}
                  className="inline-flex items-center gap-1 text-teal hover:underline break-all"
                  title="Написать студенту на email"
                >
                  <Icons.mail size={13} className="shrink-0" />
                  {submission.studentEmail}
                </a>
              )}
            </div>
          )}

          <a
            href={submission.payload}
            target="_blank"
            rel="noopener noreferrer"
            className="mt-1 flex items-start gap-1 text-xs text-teal hover:underline"
          >
            <Icons.externalLink size={11} className="mt-0.5 shrink-0" />
            <span className="min-w-0 break-all">{submission.payload}</span>
          </a>

          {isInReview && submission.reviewerId && (
            <div className="mt-2 flex items-center gap-2.5 p-3 rounded-lg bg-blue/8 text-sm text-muted-foreground">
              <UserAvatar
                name={submission.reviewerName ?? submission.reviewerUsername}
                avatarId={submission.reviewerAvatarId}
                userId={submission.reviewerId}
                className="size-6 shrink-0"
              />
              <div>
                <span className="font-medium text-foreground">Сейчас в ревью:</span>{" "}
                {submission.reviewerName ??
                  submission.reviewerUsername ??
                  `reviewer ${getShortId(submission.reviewerId)}`}
              </div>
            </div>
          )}

          {submission.authorHelpRequestedAt && submission.authorHelpMessage && (
            <div
              className="mt-2 p-3 rounded-lg bg-yellow/10 border border-yellow/30 text-sm"
              data-testid="author-help-message"
            >
              <span className="inline-flex items-center gap-1 font-medium text-yellow">
                <Icons.help size={14} /> Просьба о помощи:
              </span>{" "}
              <span className="whitespace-pre-wrap break-words text-foreground">
                {submission.authorHelpMessage}
              </span>
            </div>
          )}

          {submission.feedback && (
            <div className="mt-2 p-3 rounded-lg bg-muted/50 text-sm text-muted-foreground">
              <span className="font-medium text-foreground">Обратная связь:</span>{" "}
              {submission.feedback}
            </div>
          )}

          <div className="mt-2 space-y-1 text-xs text-muted-foreground">
            <p>Отправлено: {formatDate(submission.submittedAt)}</p>
            {submission.reviewStartedAt && (
              <p>Ревью начато: {formatDate(submission.reviewStartedAt)}</p>
            )}
            {submission.reviewedAt && <p>Проверено: {formatDate(submission.reviewedAt)}</p>}
          </div>

          {hasMultipleAttempts ? (
            <div className="mt-3">
              <AttemptHistory
                submissionId={submission.submissionId}
                currentAttemptNumber={submission.submissionNo}
              />
            </div>
          ) : null}

          {canUseAiReview ? (
            <div className="mt-3">
              <AiReviewHistory submissionId={submission.submissionId} />
            </div>
          ) : null}
        </div>

        <div className="flex items-center gap-2 shrink-0 flex-wrap">
          {canStartReview && (
            <Button
              size="sm"
              variant="outline"
              onClick={() => onStartReview(submission)}
              disabled={isActionDisabled}
            >
              <Icons.play size={14} /> Начать ревью
            </Button>
          )}

          {canReview && (
            <>
              <Button
                size="sm"
                className="bg-teal text-primary-foreground border-0 hover:opacity-90"
                onClick={() => onApprove(submission)}
                disabled={isActionDisabled}
              >
                <Icons.check size={14} /> Принять
              </Button>
              <Button
                size="sm"
                variant="outline"
                className="text-red border-red/30 hover:bg-red/10"
                onClick={() => onRequestChanges(submission)}
                disabled={isActionDisabled}
              >
                <Icons.message size={14} /> Доработать
              </Button>
              <Button
                size="sm"
                variant="outline"
                onClick={() => onCancelReview(submission)}
                disabled={isActionDisabled}
                title="Снять себя как проверяющего и вернуть работу в статус «Ожидает проверки»"
              >
                <Icons.undo size={14} /> Отказаться от проверки
              </Button>
            </>
          )}

          {canMarkComplete && (
            <Button
              size="sm"
              variant="outline"
              className="text-green border-green/30 hover:bg-green/10"
              onClick={() => onMarkComplete(submission)}
              disabled={isActionDisabled}
              title="Принять работу вручную и засчитать задание — независимо от статуса AI-проверки"
            >
              <Icons.completed size={14} /> Отметить выполненным
            </Button>
          )}

          <Button
            size="sm"
            variant="outline"
            onClick={() => onOpenStatusOverride(submission)}
            disabled={isActionDisabled}
            title="Изменить статус задания у студента"
          >
            <Icons.settings size={14} /> Статус
          </Button>

          {isReviewed && (
            <Button
              size="sm"
              variant="outline"
              onClick={() => onReopen(submission)}
              disabled={isActionDisabled}
              title="Вернуть работу в статус «На проверке», чтобы переоценить решение"
            >
              <Icons.refresh size={14} /> Вернуть в ревью
            </Button>
          )}
        </div>
      </div>
    </Card>
  );
}
