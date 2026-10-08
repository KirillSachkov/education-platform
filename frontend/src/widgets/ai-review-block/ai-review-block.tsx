"use client";

import {
  aiReviewBySubmissionQueryOptions,
  IterationTimeline,
  useStudentRerunReview,
  type AiReviewDetailDto,
  type AiReviewIterationDto,
  type AiReviewStatus,
  type AiReviewVerdict,
} from "@/entities/ai-review";
import { useFinalizeSubmission } from "@/features/finalize-submission";
import { IterationFeedbackButtons } from "@/features/iteration-feedback";
import { isEnvelopeError } from "@/shared/api";
import { cn } from "@/shared/lib/css";
import { Icons } from "@/shared/ui/icons";
import { Button } from "@/shared/ui/kit/button";
import { Card, CardContent } from "@/shared/ui/kit/card";
import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import type { ReactNode } from "react";

interface AiReviewBlockProps {
  submissionId: string;
  /** True если submission всё ещё в open status (PENDING/IN_REVIEW). Гейтит FAILED-кнопку «Отправить автору». */
  isOpen: boolean;
  /**
   * True если задание уже зачтено (IssueProgress COMPLETED). Разрешает опциональную
   * доработку после MINOR-approve (#725) и меняет копирайт баннера на «зачёт сохранён»,
   * если пост-approve ре-ревью вдруг вернуло серьёзные замечания.
   */
  isCompleted: boolean;
}

export function AiReviewBlock({ submissionId, isOpen, isCompleted }: AiReviewBlockProps) {
  const query = useQuery({
    ...aiReviewBySubmissionQueryOptions(submissionId),
    // Пока итерация в полёте — поллим статус (issue-history поллит только UNDER_REVIEW,
    // а пост-approve ре-ревью (#725) идёт на COMPLETED-задании, вне того polling'а).
    // TanStack типизирует state.data то как raw Envelope, то как selected DTO (base vs
    // strict tsconfig расходятся) — читаем статус из обоих вариантов, чтобы не зависеть.
    refetchInterval: (q) => {
      const data = q.state.data as
        | { status?: AiReviewStatus; result?: { status?: AiReviewStatus } }
        | undefined;
      const status = data?.status ?? data?.result?.status;
      return status === "QUEUED" || status === "RUNNING" ? 4000 : false;
    },
  });

  if (query.isPending) {
    return (
      <Card className="rounded-2xl border-border/60">
        <CardContent className="flex items-center gap-2 py-6 text-sm text-muted-foreground">
          <Icons.loading className="size-4 animate-spin" />
          Загружаем состояние AI-проверки…
        </CardContent>
      </Card>
    );
  }

  if (query.isError) {
    const code = isEnvelopeError(query.error) ? query.error.messages[0]?.code : undefined;

    if (code === "review.not_found") {
      return <NoReviewState />;
    }

    return (
      <Card className="rounded-2xl border-red-muted bg-red-dim/40">
        <CardContent className="py-6 text-sm text-red">
          Не удалось загрузить статус AI-проверки. Попробуй обновить страницу.
        </CardContent>
      </Card>
    );
  }

  return (
    <ReviewView
      review={query.data}
      submissionId={submissionId}
      isOpen={isOpen}
      isCompleted={isCompleted}
    />
  );
}

function ShellHeader({ status, prSlot }: { status?: AiReviewStatus; prSlot?: ReactNode }) {
  return (
    <div className="flex flex-col gap-3 border-b border-border/50 bg-muted/30 px-5 py-3.5 sm:flex-row sm:items-center sm:justify-between">
      <div className="flex items-center gap-2 text-sm font-semibold text-foreground">
        <span className="inline-flex size-7 items-center justify-center rounded-lg bg-primary/10 text-primary">
          <Icons.ai className="size-4" />
        </span>
        AI-проверка
        {status === "RUNNING" ? (
          <Icons.loading className="size-3.5 animate-spin text-muted-foreground" />
        ) : null}
      </div>
      {prSlot}
    </div>
  );
}

function PrLink({ review }: { review: AiReviewDetailDto }) {
  return (
    <a
      href={review.pullRequestUrl}
      target="_blank"
      rel="noreferrer"
      className={cn(
        "inline-flex items-center gap-1.5 rounded-lg border border-border/60 bg-card px-2.5 py-1.5",
        "text-xs font-medium text-foreground transition-colors hover:border-primary/40 hover:text-primary",
      )}
    >
      <Icons.github className="size-3.5" />
      PR #{review.pullNumber} в{" "}
      <span className="font-mono text-muted-foreground">{review.repoFullName}</span>
      <Icons.externalLink className="size-3 text-muted-foreground" />
    </a>
  );
}

function NoReviewState() {
  return (
    <Card className="overflow-hidden rounded-2xl border-border/60 p-0">
      <ShellHeader />
      <CardContent className="space-y-3 py-5 text-sm text-muted-foreground">
        <p>
          AI-проверка ещё не запущена для этой попытки. Если решение — PR на GitHub, она появится
          автоматически после отправки задачи.
        </p>
        <p>
          Если AI-проверка не появилась — проверь, что{" "}
          <Link href="/settings/integrations" className="font-medium text-primary hover:underline">
            GitHub App подключён
          </Link>{" "}
          для нужного аккаунта или организации.
        </p>
      </CardContent>
    </Card>
  );
}

interface ReviewViewProps {
  review: AiReviewDetailDto;
  submissionId: string;
  isOpen: boolean;
  isCompleted: boolean;
}

function ReviewView({ review, submissionId, isOpen, isCompleted }: ReviewViewProps) {
  const finalizeMutation = useFinalizeSubmission(submissionId);
  const rerunMutation = useStudentRerunReview({ reviewId: review.id, submissionId });

  const lastIteration = review.iterations[review.iterations.length - 1];

  // #725 + #976: доработка доступна, пока задание зачтено и AI уже выносила вердикт.
  // Гейт на MINOR_ISSUES делал петлю одноразовой — после re-run'а с MAJOR (или упавшей
  // итерации, где latestVerdict сбрасывается в null) кнопка исчезала навсегда, а
  // ре-сабмит невозможен: задание уже APPROVED. Зачёт неотбираем, поэтому перепроверять
  // можно сколько нужно. LOOKS_GOOD исключён — там дорабатывать нечего.
  const isRerunning =
    rerunMutation.isPending || review.status === "QUEUED" || review.status === "RUNNING";
  const canRerun =
    isCompleted &&
    review.latestVerdict !== "LOOKS_GOOD" &&
    review.iterations.some((iteration) => iteration.status === "COMPLETED");

  return (
    <Card
      className="overflow-hidden rounded-2xl border-border/60 p-0"
      data-testid="ai-review-block"
    >
      <ShellHeader status={review.status} prSlot={<PrLink review={review} />} />

      <CardContent className="space-y-4 py-5">
        <GuidanceBanner
          status={review.status}
          lastIteration={lastIteration}
          isCompleted={isCompleted}
        />

        {review.iterations.length === 0 ? (
          <p className="text-sm text-muted-foreground">
            {review.status === "RUNNING"
              ? "AI читает diff и оставляет комментарии в PR — это займёт минуту."
              : "Пока нет ни одной AI-итерации. Она появится автоматически после проверки PR."}
          </p>
        ) : (
          <IterationTimeline
            iterations={review.iterations}
            pullRequestUrl={review.pullRequestUrl}
            renderFeedback={(iteration) => <IterationFeedbackButtons iterationId={iteration.id} />}
          />
        )}

        {isOpen && review.status === "FAILED" ? (
          <div className="flex flex-col gap-2 border-t border-border/50 pt-4 sm:flex-row sm:items-center sm:justify-between">
            <p className="text-xs text-muted-foreground">
              AI не смогла проверить. Можешь отправить решение автору на ручную проверку.
            </p>
            <Button
              type="button"
              size="sm"
              variant="secondary"
              onClick={() => finalizeMutation.mutate()}
              disabled={finalizeMutation.isPending}
              data-testid="finalize-button"
            >
              {finalizeMutation.isPending ? (
                <>
                  <Icons.loading className="mr-1.5 size-4 animate-spin" />
                  Отправляем…
                </>
              ) : (
                <>
                  <Icons.send className="mr-1.5 size-4" />
                  Отправить автору
                </>
              )}
            </Button>
          </div>
        ) : null}

        {/* #725 — опциональная доработка: задание принято с MINOR-замечаниями, но
            студент может закрыть их и перепроверить. Зачёт при этом не теряется. */}
        {canRerun ? (
          <div className="flex flex-col gap-2 border-t border-border/50 pt-4 sm:flex-row sm:items-center sm:justify-between">
            <p className="text-xs text-muted-foreground">
              Доработал замечания в PR? Запусти повторную проверку — зачёт останется в любом случае.
            </p>
            <Button
              type="button"
              size="sm"
              variant="secondary"
              onClick={() => {
                rerunMutation.mutate();
              }}
              disabled={isRerunning}
              aria-busy={isRerunning}
              data-testid="student-rerun-button"
            >
              {isRerunning ? (
                <>
                  <Icons.loading className="mr-1.5 size-4 animate-spin" />
                  Проверяем…
                </>
              ) : (
                <>
                  <Icons.refresh className="mr-1.5 size-4" />
                  Проверить снова
                </>
              )}
            </Button>
          </div>
        ) : null}
      </CardContent>
    </Card>
  );
}

type BannerTone = "info" | "success" | "warning" | "danger";

const BANNER_TONE: Record<BannerTone, { wrap: string; icon: string; title: string }> = {
  info: {
    wrap: "border-blue/25 bg-blue/10",
    icon: "text-blue",
    title: "text-blue",
  },
  success: {
    wrap: "border-green-muted bg-green-dim",
    icon: "text-green",
    title: "text-green",
  },
  warning: {
    wrap: "border-yellow-muted bg-yellow-dim",
    icon: "text-yellow",
    title: "text-yellow",
  },
  danger: {
    wrap: "border-red-muted bg-red-dim",
    icon: "text-red",
    title: "text-red",
  },
};

function GuidanceBanner({
  status,
  lastIteration,
  isCompleted,
}: {
  status: AiReviewStatus;
  lastIteration: AiReviewIterationDto | undefined;
  isCompleted: boolean;
}) {
  const content = resolveGuidance(status, lastIteration, isCompleted);
  if (!content) return null;

  const tone = BANNER_TONE[content.tone];

  return (
    <div
      className={cn("flex gap-3 rounded-xl border p-3.5", tone.wrap)}
      data-testid="guidance-banner"
      data-tone={content.tone}
    >
      <span className={cn("mt-0.5 shrink-0", tone.icon)}>
        {content.spinning ? (
          <Icons.loading className="size-4 animate-spin" />
        ) : (
          <content.Icon className="size-4" />
        )}
      </span>
      <div className="space-y-1 text-sm">
        <p className={cn("font-semibold", tone.title)}>{content.title}</p>
        {content.body ? <div className="text-foreground/80">{content.body}</div> : null}
      </div>
    </div>
  );
}

interface GuidanceContent {
  tone: BannerTone;
  Icon: (typeof Icons)[keyof typeof Icons];
  spinning?: boolean;
  title: string;
  body?: ReactNode;
}

function resolveGuidance(
  status: AiReviewStatus,
  lastIteration: AiReviewIterationDto | undefined,
  isCompleted: boolean,
): GuidanceContent | null {
  if (status === "RUNNING") {
    return {
      tone: "info",
      Icon: Icons.loading,
      spinning: true,
      title: "AI проверяет ваш PR…",
      body: <p>Обычно это занимает меньше минуты. Результат появится здесь автоматически.</p>,
    };
  }

  if (status === "FAILED") {
    if (lastIteration?.failureReason === "review.no_installation") {
      return {
        tone: "warning",
        Icon: Icons.github,
        title: "GitHub App не подключён",
        body: (
          <p>
            <Link
              href="/settings/integrations"
              className="font-medium underline underline-offset-2"
            >
              Подключи интеграцию
            </Link>{" "}
            чтобы AI могла проверять твои PR. Либо нажми «Отправить автору» — он посмотрит решение
            вручную.
          </p>
        ),
      };
    }
    return {
      tone: "danger",
      Icon: Icons.warning,
      title: "AI не смогла проверить",
      body: (
        <p>
          Что-то пошло не так с автопроверкой. Нажми{" "}
          <span className="font-medium text-foreground">«Отправить автору»</span> — он посмотрит
          твоё решение вручную.
        </p>
      ),
    };
  }

  // READY — verdict-driven guidance.
  const verdict: AiReviewVerdict | null = lastIteration?.verdict ?? null;

  if (verdict === "LOOKS_GOOD") {
    return {
      tone: "success",
      Icon: Icons.success,
      title: "Принято автоматически",
      body: (
        <p>
          AI не нашла замечаний — задача засчитана. Отличная работа!{" "}
          <span className="text-muted-foreground">
            AI читает код статически и не запускает его — на всякий случай проверь, что всё
            работает, руками.
          </span>
        </p>
      ),
    };
  }

  if (verdict === "MINOR_ISSUES") {
    // #383: MINOR авто-принимается. #725: переотправлять не обязательно, но студент
    // может закрыть замечания и перепроверить кнопкой «Проверить снова» ниже.
    return {
      tone: "success",
      Icon: Icons.success,
      title: "Принято — есть необязательные замечания",
      body: (
        <p>
          Задача засчитана автоматически. В PR есть пара необязательных замечаний —{" "}
          <span className="font-medium text-foreground">переотправлять не обязательно</span>. Если
          хочешь довести до идеала — доработай их и нажми «Проверить снова», зачёт останется.{" "}
          <span className="text-muted-foreground">
            AI читает код статически и не запускает его — на всякий случай проверь, что всё
            работает, руками.
          </span>
        </p>
      ),
    };
  }

  if (verdict === "MAJOR_ISSUES" || verdict === "OFF_TOPIC") {
    // #725: если задание уже зачтено (пост-approve ре-ревью вдруг нашло серьёзные
    // замечания) — НЕ пугаем «отправьте снова», зачёт сохранён. #976: петля не
    // закрывается, кнопка «Проверить снова» остаётся доступной — на неё и указываем.
    if (isCompleted) {
      return {
        tone: "info",
        Icon: Icons.ai,
        title: "Повторная проверка нашла замечания",
        body: (
          <p>
            Задача остаётся зачтённой, доп. проверка ничего не отбирает. Хочешь довести до идеала:
            поправь замечания в PR и нажми «Проверить снова», зачёт останется.
          </p>
        ),
      };
    }
    return {
      tone: "danger",
      Icon: Icons.warning,
      title: "Нужны доработки",
      body: (
        <p>
          Внесите изменения в PR (новыми коммитами) и{" "}
          <span className="font-medium text-foreground">отправьте задачу снова</span> — AI
          перепроверит обновлённый diff.
        </p>
      ),
    };
  }

  return null;
}
