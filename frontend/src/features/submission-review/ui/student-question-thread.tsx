"use client";

import { useReplyToStudentMessage, type StudentPrMessageDto } from "@/entities/ai-review";
import { formatRelativeDate } from "@/shared/lib/date";
import { Icons } from "@/shared/ui/icons";
import { Button } from "@/shared/ui/kit/button";
import { Textarea } from "@/shared/ui/kit/textarea";
import { useState } from "react";

interface StudentQuestionThreadProps {
  submissionId: string;
  /** Реплики студента в PR — порядок по createdAt возрастанию (как отдаёт backend). */
  messages: StudentPrMessageDto[];
}

/**
 * #713 — тред реплик/вопросов студента в его PR (обратный канал к AI-ревью) с полем
 * ответа автора. Рендерится в раскрытой AI-истории карточки проверки. Пусто → секции
 * нет. Ответ автора постится в тот же тред PR через entity-мутацию
 * `useReplyToStudentMessage`, которая инвалидирует detail-кэш → answeredAt/answerBody
 * подтягиваются рефетчем.
 */
export function StudentQuestionThread({ submissionId, messages }: StudentQuestionThreadProps) {
  if (messages.length === 0) return null;

  return (
    <section
      className="rounded-xl border border-border/60 bg-card p-3 sm:p-4"
      data-testid="student-question-thread"
    >
      <h4 className="mb-3 inline-flex items-center gap-1.5 text-sm font-medium text-foreground">
        <span className="inline-flex size-6 items-center justify-center rounded-md bg-blue/10 text-blue">
          <Icons.help className="size-3.5" />
        </span>
        Вопросы студента
        <span className="text-xs font-normal text-muted-foreground">({messages.length})</span>
      </h4>

      <ol className="space-y-3">
        {messages.map((message) => (
          <li key={message.id}>
            <StudentQuestionItem message={message} submissionId={submissionId} />
          </li>
        ))}
      </ol>
    </section>
  );
}

function StudentQuestionItem({
  message,
  submissionId,
}: {
  message: StudentPrMessageDto;
  submissionId: string;
}) {
  const context =
    message.path && message.line != null
      ? `${message.path}:${String(message.line)}`
      : (message.path ?? null);

  return (
    <article
      className="rounded-lg border border-border/60 bg-muted/20 p-3"
      data-testid="student-question-item"
    >
      <header className="flex flex-wrap items-center gap-x-2 gap-y-1 text-xs text-muted-foreground">
        <span className="inline-flex min-w-0 items-center gap-1 font-medium text-foreground">
          <Icons.github className="size-3.5 shrink-0" />
          <span className="truncate">@{message.authorGithubLogin}</span>
        </span>
        <span aria-hidden className="text-border">
          ·
        </span>
        <span>{formatRelativeDate(message.createdAt)}</span>
      </header>

      {context ? (
        <p className="mt-1.5 truncate font-mono text-[11px] text-muted-foreground" title={context}>
          {context}
        </p>
      ) : null}

      <p className="mt-2 whitespace-pre-wrap break-words text-sm leading-relaxed text-foreground/90">
        {message.body}
      </p>

      {message.commentUrl ? (
        <a
          href={message.commentUrl}
          target="_blank"
          rel="noreferrer"
          className="mt-2 inline-flex items-center gap-1 text-xs font-medium text-primary transition-colors hover:text-primary/80 hover:underline"
          data-testid="student-question-pr-link"
        >
          Открыть в PR
          <Icons.externalLink className="size-3.5" />
        </a>
      ) : null}

      {message.answeredAt ? (
        <AnswerBlock body={message.answerBody} answeredAt={message.answeredAt} />
      ) : (
        <StudentReplyForm messageId={message.id} submissionId={submissionId} />
      )}
    </article>
  );
}

function AnswerBlock({ body, answeredAt }: { body: string | null; answeredAt: string }) {
  return (
    <div
      className="mt-3 rounded-lg border border-green-muted bg-green-dim p-3"
      data-testid="student-answer"
    >
      <span className="inline-flex items-center gap-1 text-xs font-medium text-green">
        <Icons.check className="size-3.5" />
        Отвечено · {formatRelativeDate(answeredAt)}
      </span>
      {body ? (
        <p className="mt-1.5 whitespace-pre-wrap break-words text-sm leading-relaxed text-foreground/90">
          {body}
        </p>
      ) : null}
    </div>
  );
}

function StudentReplyForm({
  messageId,
  submissionId,
}: {
  messageId: string;
  submissionId: string;
}) {
  const [body, setBody] = useState("");
  const mutation = useReplyToStudentMessage(submissionId);

  const trimmed = body.trim();
  const canSubmit = trimmed.length > 0 && !mutation.isPending;

  const handleSubmit = () => {
    if (!canSubmit) return;
    mutation.mutate(
      { messageId, body: trimmed },
      {
        onSuccess: () => {
          setBody("");
        },
      },
    );
  };

  return (
    <div className="mt-3 space-y-2">
      <Textarea
        value={body}
        onChange={(e) => {
          setBody(e.target.value);
        }}
        placeholder="Ответить студенту — ответ уйдёт в тред PR…"
        maxLength={20000}
        rows={2}
        disabled={mutation.isPending}
        className="text-sm"
        data-testid="student-reply-input"
      />
      <div className="flex justify-end">
        <Button
          type="button"
          size="sm"
          onClick={handleSubmit}
          disabled={!canSubmit}
          data-testid="student-reply-submit"
        >
          {mutation.isPending ? (
            <>
              <Icons.loading className="mr-1.5 size-4 animate-spin" />
              Отправляем…
            </>
          ) : (
            <>
              <Icons.send className="mr-1.5 size-4" />
              Ответить
            </>
          )}
        </Button>
      </div>
    </div>
  );
}
