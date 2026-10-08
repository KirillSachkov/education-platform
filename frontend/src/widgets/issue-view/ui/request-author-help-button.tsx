"use client";

import { useState } from "react";

import { useRequestAuthorHelp } from "@/features/request-author-help";
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

interface RequestAuthorHelpButtonProps {
  submissionId: string;
  /** Серверное состояние: автора уже звали (timestamp) → показываем «Автор позван». */
  alreadyRequestedAt: string | null;
}

const MESSAGE_MAX_LENGTH = 2000;

/**
 * #383 «Позвать автора» + #575 — студент явно подключает автора курса к проверке своего
 * решения, когда AI-ассистент не справился / нужна живая помощь. Диалог даёт опционально
 * описать, в чём именно нужна помощь — этот текст уходит автору (в т.ч. в Telegram). По
 * умолчанию автор вне цикла. После успеха (или если сервер уже знает о зове) показываем
 * спокойное подтверждение «Автор позван» вместо повторного CTA.
 */
export function RequestAuthorHelpButton({
  submissionId,
  alreadyRequestedAt,
}: RequestAuthorHelpButtonProps) {
  const mutation = useRequestAuthorHelp(submissionId);
  const [open, setOpen] = useState(false);
  const [message, setMessage] = useState("");
  const called = alreadyRequestedAt != null || mutation.isSuccess;

  if (called) {
    return (
      <p
        className="inline-flex items-center gap-1.5 text-sm text-muted-foreground"
        data-testid="author-help-requested-state"
      >
        <Icons.check className="size-4 text-teal" />
        Автор позван — он подключится к проверке.
      </p>
    );
  }

  function submit() {
    mutation.mutate(message.trim() || undefined, {
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
          data-testid="request-author-help-button"
        >
          <Icons.help className="mr-1.5 size-4" />
          Позвать автора
        </Button>
      </DialogTrigger>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Позвать автора</DialogTitle>
          <DialogDescription>
            Автор подключится к проверке вашего решения. Опишите, в чём именно нужна
            помощь — так автору будет проще помочь (необязательно).
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-2">
          <Label htmlFor="author-help-message">В чём нужна помощь?</Label>
          <Textarea
            id="author-help-message"
            value={message}
            onChange={(event) => setMessage(event.target.value)}
            maxLength={MESSAGE_MAX_LENGTH}
            rows={4}
            placeholder="Например: не проходит тест X, не понимаю требование Y, посмотрите мой подход к Z…"
            disabled={mutation.isPending}
            data-testid="author-help-message-input"
          />
          <p className="text-right text-xs text-muted-foreground">
            {message.length}/{MESSAGE_MAX_LENGTH}
          </p>
        </div>

        <DialogFooter>
          <DialogClose asChild>
            <Button type="button" variant="ghost" disabled={mutation.isPending}>
              Отмена
            </Button>
          </DialogClose>
          <Button
            type="button"
            onClick={submit}
            disabled={mutation.isPending}
            data-testid="request-author-help-submit"
          >
            {mutation.isPending ? (
              <>
                <Icons.loading className="mr-1.5 size-4 animate-spin" />
                Зовём…
              </>
            ) : (
              <>
                <Icons.help className="mr-1.5 size-4" />
                Позвать автора
              </>
            )}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
