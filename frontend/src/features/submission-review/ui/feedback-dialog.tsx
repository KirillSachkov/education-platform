"use client";

import { useState } from "react";
import { Button } from "@/shared/ui/kit/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/shared/ui/kit/dialog";
import { Textarea } from "@/shared/ui/kit/textarea";

export type FeedbackDialogIntent = "approve" | "requestChanges" | "markComplete";

interface FeedbackDialogProps {
  open: boolean;
  intent: FeedbackDialogIntent;
  onOpenChange: (open: boolean) => void;
  onSubmit: (feedback: string | null) => Promise<void>;
  isPending: boolean;
  submissionLabel: string;
}

const COPY: Record<
  FeedbackDialogIntent,
  {
    title: string;
    description: (label: string) => string;
    placeholder: string;
    submit: string;
  }
> = {
  approve: {
    title: "Принять работу",
    description: (label) =>
      `Можно оставить комментарий по работе ${label} — необязательно.`,
    placeholder: "Например: классно расписал доменные модели, поправил бы только…",
    submit: "Принять",
  },
  requestChanges: {
    title: "Отправить на доработку",
    description: (label) =>
      `Комментарий по работе ${label} не обязателен — обсудите вживую, если удобнее.`,
    placeholder: "Опишите, что нужно исправить…",
    submit: "Отправить",
  },
  markComplete: {
    title: "Отметить выполненным",
    description: (label) =>
      `Можно оставить комментарий по работе ${label} — он будет виден студенту.`,
    placeholder: "Например: засчитываю, но обрати внимание на…",
    submit: "Отметить",
  },
};

export function FeedbackDialog({
  open,
  intent,
  onOpenChange,
  onSubmit,
  isPending,
  submissionLabel,
}: FeedbackDialogProps) {
  const [feedback, setFeedback] = useState("");

  const copy = COPY[intent];

  const handleOpenChange = (nextOpen: boolean) => {
    if (!nextOpen) {
      setFeedback("");
    }

    onOpenChange(nextOpen);
  };

  const handleSubmit = async () => {
    const trimmed = feedback.trim();
    await onSubmit(trimmed.length === 0 ? null : trimmed);
    setFeedback("");
  };

  return (
    <Dialog open={open} onOpenChange={handleOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{copy.title}</DialogTitle>
          <DialogDescription>{copy.description(submissionLabel)}</DialogDescription>
        </DialogHeader>
        <Textarea
          placeholder={copy.placeholder}
          value={feedback}
          onChange={(e) => setFeedback(e.target.value)}
          maxLength={20000}
          rows={6}
        />
        <DialogFooter>
          <Button
            variant="outline"
            onClick={() => handleOpenChange(false)}
            disabled={isPending}
          >
            Отмена
          </Button>
          <Button onClick={handleSubmit} disabled={isPending}>
            {copy.submit}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
