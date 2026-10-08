"use client";

import { useState } from "react";

import { useQuery } from "@tanstack/react-query";

import {
  authorQuestionStateQueryOptions,
  useAskAuthorQuestion,
} from "@/features/ask-author-question";
import { Icons } from "@/shared/ui/icons";
import { Button } from "@/shared/ui/kit/button";
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/shared/ui/kit/dialog";
import { Label } from "@/shared/ui/kit/label";
import { Textarea } from "@/shared/ui/kit/textarea";

interface AskAuthorQuestionButtonProps {
  issueId: string;
  /** Рендерить только для авторизованного пользователя — GET состояния требует auth. */
  enabled: boolean;
}

const MESSAGE_MAX_LENGTH = 2000;

/**
 * #693 «Задать вопрос автору» — приватный issue-scoped аналог «Позвать автора» (#383),
 * доступный ДО отправки решения. Студент, зависший на чтении непонятного задания, явно
 * зовёт автора и описывает вопрос (текст обязателен — это и есть суть). После успеха (или
 * если сервер уже знает о вопросе) показываем спокойное подтверждение «Вопрос отправлен
 * автору» вместо повторного CTA.
 */
export function AskAuthorQuestionButton({ issueId, enabled }: AskAuthorQuestionButtonProps) {
  const { data: state } = useQuery({
    ...authorQuestionStateQueryOptions(issueId),
    enabled,
  });
  const mutation = useAskAuthorQuestion(issueId);
  const [open, setOpen] = useState(false);
  const [message, setMessage] = useState("");

  const asked = state?.askedAt != null || mutation.isSuccess;

  if (asked) {
    return (
      <p
        className="inline-flex items-center gap-1.5 text-sm text-muted-foreground"
        data-testid="author-question-asked-state"
      >
        <Icons.check className="size-4 text-teal" />
        Вопрос отправлен автору — он ответит, как только сможет.
      </p>
    );
  }

  const trimmed = message.trim();

  function submit() {
    if (trimmed.length === 0) return;
    mutation.mutate(trimmed, {
      onSuccess: () => setOpen(false),
    });
  }

  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger asChild>
        <Button
          type="button"
          size="sm"
          variant="outline"
          className="min-h-11"
          data-testid="ask-author-question-button"
        >
          <Icons.message className="mr-1.5 size-4" />
          Задать вопрос автору
        </Button>
      </DialogTrigger>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Задать вопрос автору</DialogTitle>
          <DialogDescription>
            Не разобрались с заданием? Опишите вопрос — автор увидит его и подключится, чтобы
            помочь. Отправлять решение для этого не нужно.
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-2">
          <Label htmlFor="ask-author-question-message">Ваш вопрос</Label>
          <Textarea
            id="ask-author-question-message"
            value={message}
            onChange={(event) => setMessage(event.target.value)}
            maxLength={MESSAGE_MAX_LENGTH}
            rows={4}
            required
            placeholder="Например: не понимаю требование X, с чего начать задачу Y, что значит формулировка Z…"
            disabled={mutation.isPending}
            data-testid="ask-author-question-input"
          />
          <p className="text-right text-xs text-muted-foreground">
            {message.length}/{MESSAGE_MAX_LENGTH}
          </p>
        </div>

        <DialogFooter>
          <DialogClose asChild>
            <Button type="button" variant="ghost" className="min-h-11" disabled={mutation.isPending}>
              Отмена
            </Button>
          </DialogClose>
          <Button
            type="button"
            className="min-h-11"
            onClick={submit}
            disabled={mutation.isPending || trimmed.length === 0}
            data-testid="ask-author-question-submit"
          >
            {mutation.isPending ? (
              <>
                <Icons.loading className="mr-1.5 size-4 animate-spin" />
                Отправляем…
              </>
            ) : (
              <>
                <Icons.message className="mr-1.5 size-4" />
                Отправить вопрос
              </>
            )}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
