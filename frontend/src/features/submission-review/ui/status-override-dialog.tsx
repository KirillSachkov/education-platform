"use client";

import { useState } from "react";
import type { IssueProgressStatus } from "@/shared/types/status";
import { Button } from "@/shared/ui/kit/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/shared/ui/kit/dialog";
import { Label } from "@/shared/ui/kit/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/shared/ui/kit/select";
import { Textarea } from "@/shared/ui/kit/textarea";

const ISSUE_STATUS_OPTIONS: { value: IssueProgressStatus; label: string }[] = [
  { value: "NOT_STARTED", label: "Не начато" },
  { value: "IN_PROGRESS", label: "В работе" },
  { value: "UNDER_REVIEW", label: "На проверке" },
  { value: "REQUESTED_CHANGES", label: "Нужны правки" },
  { value: "COMPLETED", label: "Выполнено" },
];

interface StatusOverrideDialogProps {
  open: boolean;
  currentStatus: IssueProgressStatus;
  submissionLabel: string;
  onOpenChange: (open: boolean) => void;
  onSubmit: (targetStatus: IssueProgressStatus, feedback: string | null) => Promise<void>;
  isPending: boolean;
}

export function StatusOverrideDialog({
  open,
  currentStatus,
  submissionLabel,
  onOpenChange,
  onSubmit,
  isPending,
}: StatusOverrideDialogProps) {
  const [targetStatus, setTargetStatus] = useState<IssueProgressStatus>(currentStatus);
  const [feedback, setFeedback] = useState("");

  const handleOpenChange = (nextOpen: boolean) => {
    if (!nextOpen) {
      setFeedback("");
      setTargetStatus(currentStatus);
    }

    onOpenChange(nextOpen);
  };

  const handleSubmit = async () => {
    const trimmed = feedback.trim();
    await onSubmit(targetStatus, trimmed.length === 0 ? null : trimmed);
    setFeedback("");
  };

  return (
    <Dialog open={open} onOpenChange={handleOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Изменить статус задания</DialogTitle>
          <DialogDescription>{submissionLabel}</DialogDescription>
        </DialogHeader>

        <div className="space-y-4">
          <div className="space-y-2">
            <Label htmlFor="review-status-override">Статус</Label>
            <Select
              value={targetStatus}
              onValueChange={(value) => setTargetStatus(value as IssueProgressStatus)}
              disabled={isPending}
            >
              <SelectTrigger id="review-status-override" className="w-full">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {ISSUE_STATUS_OPTIONS.map((option) => (
                  <SelectItem key={option.value} value={option.value}>
                    {option.label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>

          {targetStatus === "COMPLETED" ? (
            <div className="space-y-2">
              <Label htmlFor="review-status-feedback">Комментарий студенту</Label>
              <Textarea
                id="review-status-feedback"
                placeholder="Например: засчитываю работу вручную, посмотри замечания на будущее…"
                value={feedback}
                onChange={(e) => setFeedback(e.target.value)}
                maxLength={20000}
                rows={5}
                disabled={isPending}
              />
            </div>
          ) : null}
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={() => handleOpenChange(false)} disabled={isPending}>
            Отмена
          </Button>
          <Button onClick={handleSubmit} disabled={isPending}>
            Изменить
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
