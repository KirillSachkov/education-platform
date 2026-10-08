"use client";

import type { OnboardingStepDto } from "@/entities/plan-onboarding";
import { Button } from "@/shared/ui/kit/button";
import { Checkbox } from "@/shared/ui/kit/checkbox";
import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/shared/ui/kit/dialog";
import { Input } from "@/shared/ui/kit/input";
import { Label } from "@/shared/ui/kit/label";
import dynamic from "next/dynamic";

const MarkdownEditor = dynamic(
  () => import("@/shared/ui/kit/markdown-editor").then((m) => ({ default: m.MarkdownEditor })),
  { ssr: false, loading: () => <div className="h-64 animate-pulse bg-muted rounded-md" /> },
);
import { useState } from "react";
import { useAddMarkdownStep, useUpdateMarkdownStep } from "../model/use-onboarding-mutations";

type Props = {
  planId: string;
  step?: OnboardingStepDto | null;
  open: boolean;
  onOpenChange: (open: boolean) => void;
};

export function MarkdownStepDialog({ planId, step, open, onOpenChange }: Props) {
  const isEditing = step !== null && step !== undefined;

  const [title, setTitle] = useState(step?.title ?? "");
  const [body, setBody] = useState(step?.body ?? "");
  const [isSkippable, setIsSkippable] = useState(step?.isSkippable ?? true);

  const addMutation = useAddMarkdownStep(planId);
  const updateMutation = useUpdateMarkdownStep(planId);
  const isPending = addMutation.isPending || updateMutation.isPending;

  // Reset state on dialog open with fresh step.
  const reset = () => {
    setTitle(step?.title ?? "");
    setBody(step?.body ?? "");
    setIsSkippable(step?.isSkippable ?? true);
  };

  const handleSubmit = () => {
    const request = { title, body, isSkippable };
    const onSuccess = () => onOpenChange(false);
    if (isEditing) {
      updateMutation.mutate({ stepId: step.id, request }, { onSuccess });
    } else {
      addMutation.mutate(request, { onSuccess });
    }
  };

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        onOpenChange(next);
        if (next) reset();
      }}
    >
      <DialogContent className="max-w-2xl">
        <DialogHeader>
          <DialogTitle>{isEditing ? "Редактировать шаг" : "Новый шаг"}</DialogTitle>
        </DialogHeader>

        <div className="space-y-4">
          <div className="space-y-1.5">
            <Label htmlFor="step-title">Заголовок</Label>
            <Input
              id="step-title"
              value={title}
              onChange={(e) => setTitle(e.target.value)}
              maxLength={200}
              placeholder="Привет, ученик"
            />
          </div>

          <div className="space-y-1.5">
            <Label>Текст</Label>
            <MarkdownEditor value={body} onChange={setBody} />
          </div>

          <label className="flex items-center gap-2 text-sm">
            <Checkbox
              checked={isSkippable}
              onCheckedChange={(checked) => setIsSkippable(checked === true)}
            />
            Можно пропустить
          </label>
        </div>

        <div className="flex justify-end gap-2 pt-2">
          <Button variant="ghost" onClick={() => onOpenChange(false)} disabled={isPending}>
            Отмена
          </Button>
          <Button onClick={handleSubmit} disabled={isPending || !title.trim() || !body.trim()}>
            {isEditing ? "Сохранить" : "Добавить"}
          </Button>
        </div>
      </DialogContent>
    </Dialog>
  );
}
