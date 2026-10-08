"use client";

import type { ReactNode } from "react";
import { Clock, MessageSquareWarning, PlayCircle, Send } from "lucide-react";
import type { IssueProgressStatus } from "@/entities/course-progress";
import type { IssueSubmissionMode } from "@/entities/issue";
import { isGitHubPullRequestUrl } from "@/entities/review-submission";
import { Button } from "@/shared/ui/kit/button";
import { Input } from "@/shared/ui/kit/input";
import { Textarea } from "@/shared/ui/kit/textarea";
import { SuccessCheck } from "@/shared/ui/components/success-check";

interface IssueSubmissionFormProps {
  status: IssueProgressStatus;
  submissionMode: IssueSubmissionMode;
  submissionText: string;
  onSubmissionTextChange: (value: string) => void;
  onSubmit: () => void;
  onStart: () => void;
  canStart: boolean;
  canSubmit: boolean;
  isSubmitting?: boolean;
  isStarting?: boolean;
  disabled?: boolean;
  helperText?: string | null;
  feedback?: string | null;
  selfCheckInstructions?: string | null;
  /**
   * Если задан — на submit-экране (IN_PROGRESS / REQUESTED_CHANGES) показывается
   * вместо поля ввода PR + кнопки «Отправить». Используется для гейта подключения
   * GitHub (студент не привязал аккаунт / не установил review-app). Фидбек по
   * REQUESTED_CHANGES остаётся виден над гейтом.
   */
  connectionGate?: ReactNode;
}

export function IssueSubmissionForm({
  status,
  submissionMode,
  submissionText,
  onSubmissionTextChange,
  onSubmit,
  onStart,
  canStart,
  canSubmit,
  isSubmitting = false,
  isStarting = false,
  disabled = false,
  helperText = null,
  feedback = null,
  selfCheckInstructions = null,
  connectionGate = null,
}: IssueSubmissionFormProps) {
  const canEdit = !disabled && (status === "IN_PROGRESS" || status === "REQUESTED_CHANGES");
  const showForm = status === "IN_PROGRESS" || status === "REQUESTED_CHANGES";
  const isSelfCheck = submissionMode === "SELF_CHECK";
  // PR-режим: показываем inline-ошибку, когда что-то введено, но это не ссылка на
  // конкретный pull request (например страница «создать PR» /pull/new/...). #718
  const showPrUrlError =
    !isSelfCheck &&
    submissionText.trim().length > 0 &&
    !isGitHubPullRequestUrl(submissionText.trim());

  if (status === "COMPLETED") {
    return (
      <div className="flex items-center gap-2">
        <SuccessCheck subtle className="size-4 text-green" />
        <span className="text-sm text-green">Завершена</span>
      </div>
    );
  }

  if (status === "NOT_STARTED") {
    return (
      <div className="flex items-center gap-3">
        <Button size="sm" onClick={onStart} disabled={!canStart || disabled || isStarting}>
          <PlayCircle size={14} strokeWidth={1.5} />
          {isStarting ? "Запускаем..." : "Взять в работу"}
        </Button>
        {disabled && helperText && (
          <span className="text-xs text-muted-foreground">{helperText}</span>
        )}
      </div>
    );
  }

  if (status === "UNDER_REVIEW") {
    return (
      <div className="flex items-center gap-2">
        <Clock size={14} strokeWidth={1.5} className="text-blue" />
        <span className="text-sm text-blue">Ожидает проверки</span>
      </div>
    );
  }

  if (showForm) {
    return (
      <div className="space-y-3">
        {status === "REQUESTED_CHANGES" && feedback && (
          <div className="px-3 py-2.5 rounded-lg bg-red-dim border border-red/20">
            <div className="flex items-start gap-2">
              <MessageSquareWarning
                size={14}
                strokeWidth={1.5}
                className="text-red shrink-0 mt-0.5"
              />
              <p className="text-sm text-foreground/85 leading-relaxed">{feedback}</p>
            </div>
          </div>
        )}
        {connectionGate ? (
          connectionGate
        ) : (
          <>
            {isSelfCheck && selfCheckInstructions && (
              <div className="rounded-lg border border-border/60 bg-muted/20 px-3 py-2.5">
                <p className="text-xs font-medium text-muted-foreground mb-1">
                  Условия самопроверки
                </p>
                <p className="whitespace-pre-wrap text-sm leading-relaxed">
                  {selfCheckInstructions}
                </p>
              </div>
            )}
            {isSelfCheck ? (
              <div className="space-y-2">
                <Textarea
                  value={submissionText}
                  onChange={(e) => onSubmissionTextChange(e.target.value)}
                  placeholder="Кратко напишите, что вы проверили перед завершением задачи"
                  rows={4}
                  disabled={!canEdit || isSubmitting || isStarting}
                />
                <Button
                  size="sm"
                  onClick={onSubmit}
                  disabled={!canEdit || !canSubmit || isSubmitting || isStarting}
                >
                  <Send size={14} strokeWidth={1.5} />
                  Проверить себя
                </Button>
              </div>
            ) : (
              <div className="space-y-1.5">
                <div className="flex items-center gap-2">
                  <Input
                    value={submissionText}
                    onChange={(e) => onSubmissionTextChange(e.target.value)}
                    placeholder="https://github.com/...pull/123"
                    className="font-mono text-sm h-9 rounded-lg flex-1"
                    disabled={!canEdit || isSubmitting || isStarting}
                    aria-invalid={showPrUrlError || undefined}
                  />
                  <Button
                    size="sm"
                    onClick={onSubmit}
                    disabled={!canEdit || !canSubmit || isSubmitting || isStarting}
                  >
                    <Send size={14} strokeWidth={1.5} />
                    {status === "REQUESTED_CHANGES" ? "Повторно" : "Отправить"}
                  </Button>
                </div>
                {showPrUrlError && (
                  <p className="text-xs text-red" data-testid="pr-url-error">
                    Нужна ссылка на конкретный pull request, напр.
                    https://github.com/owner/repo/pull/123
                  </p>
                )}
              </div>
            )}
            {disabled && helperText && (
              <p className="text-xs text-muted-foreground">{helperText}</p>
            )}
          </>
        )}
      </div>
    );
  }

  return null;
}
