"use client";

import { reviewSpecQueryOptions } from "@/entities/issue";
import type { ReviewSpecDto } from "@/entities/issue";
import { Button } from "@/shared/ui/kit/button";
import { Label } from "@/shared/ui/kit/label";
import { Switch } from "@/shared/ui/kit/switch";
import { Textarea } from "@/shared/ui/kit/textarea";
import { Icons } from "@/shared/ui/icons";
import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { useUpdateReviewSpec } from "../model/use-update-review-spec";

const AUTHOR_PROMPT_MAX = 10_000;
const REVIEW_ASPECTS_MAX = 10_000;

interface Props {
  issueId: string;
}

interface FormState {
  authorPrompt: string;
  reviewAspects: string;
  isAutoReviewEnabled: boolean;
}

function deriveFromData(data: ReviewSpecDto | null | undefined): FormState {
  return {
    authorPrompt: data?.authorPrompt ?? "",
    reviewAspects: data?.reviewAspects ?? "",
    isAutoReviewEnabled: data?.isAutoReviewEnabled ?? true,
  };
}

/**
 * Per-issue ReviewSpec editor. Загружает текущий spec (или пустую форму если
 * spec ещё не создан), даёт автору задать AuthorPrompt + ReviewAspects + toggle.
 * Используется на edit-issue-form в sheet'е course-builder'а.
 *
 * Form-state pattern: `draft ?? deriveFromData(data)`. Draft = null означает
 * «use fetched data as-is»; user edits → draft устанавливается, save → reset
 * draft в null (UI снова показывает свежие данные).
 */
export function ReviewSpecSection({ issueId }: Props) {
  const { data, isLoading } = useQuery(reviewSpecQueryOptions(issueId));
  const mutation = useUpdateReviewSpec(issueId);
  const [draft, setDraft] = useState<FormState | null>(null);
  const form = draft ?? deriveFromData(data);
  const isDirty = draft !== null;

  const update = (patch: Partial<FormState>) => setDraft({ ...form, ...patch });

  const handleSave = () => {
    mutation.mutate(
      {
        authorPrompt: form.authorPrompt.trim() || null,
        reviewAspects: form.reviewAspects.trim() || null,
        isAutoReviewEnabled: form.isAutoReviewEnabled,
      },
      {
        onSuccess: () => setDraft(null),
      },
    );
  };

  return (
    <div className="space-y-4 rounded-lg border border-border/50 p-4">
      <div className="flex items-center gap-2">
        <Icons.ai size={16} className="text-primary" />
        <h3 className="text-sm font-semibold">AI-проверка</h3>
      </div>
      <p className="text-xs text-muted-foreground">
        Эти инструкции попадут в prompt AI-проверяльщика при ревью PR студента.
        Прячьте детали реализации — указывайте, на что обратить внимание.
      </p>

      <div className="space-y-2">
        <div className="flex items-center justify-between gap-3">
          <Label htmlFor={`review-spec-author-prompt-${issueId}`} className="text-sm">
            Инструкции для AI (опционально)
          </Label>
          <span className="text-xs text-muted-foreground">
            {form.authorPrompt.length} / {AUTHOR_PROMPT_MAX}
          </span>
        </div>
        <Textarea
          id={`review-spec-author-prompt-${issueId}`}
          value={form.authorPrompt}
          onChange={(e) => update({ authorPrompt: e.target.value })}
          placeholder="Например: «Проверь, что нет SQL injection. Убедись, что DTO не утекают наружу domain entities.»"
          rows={4}
          maxLength={AUTHOR_PROMPT_MAX}
          disabled={isLoading || mutation.isPending}
        />
      </div>

      <div className="space-y-2">
        <div className="flex items-center justify-between gap-3">
          <Label htmlFor={`review-spec-aspects-${issueId}`} className="text-sm">
            На что обратить внимание (опционально)
          </Label>
          <span className="text-xs text-muted-foreground">
            {form.reviewAspects.length} / {REVIEW_ASPECTS_MAX}
          </span>
        </div>
        <Textarea
          id={`review-spec-aspects-${issueId}`}
          value={form.reviewAspects}
          onChange={(e) => update({ reviewAspects: e.target.value })}
          placeholder="Например: «Тесты. Обработка ошибок. Идемпотентность endpoint'ов.»"
          rows={3}
          maxLength={REVIEW_ASPECTS_MAX}
          disabled={isLoading || mutation.isPending}
        />
      </div>

      <div className="flex items-center justify-between gap-3 pt-2 border-t border-border/30">
        <div className="space-y-0.5">
          <Label htmlFor={`review-spec-toggle-${issueId}`} className="text-sm">
            Кнопка «Запустить AI» для студента
          </Label>
          <p className="text-xs text-muted-foreground">
            Выключи, чтобы временно скрыть AI-проверку у студентов для этой задачи.
          </p>
        </div>
        <Switch
          id={`review-spec-toggle-${issueId}`}
          checked={form.isAutoReviewEnabled}
          onCheckedChange={(value) => update({ isAutoReviewEnabled: value })}
          disabled={isLoading || mutation.isPending}
        />
      </div>

      <div className="flex justify-end gap-2 pt-2">
        <Button
          type="button"
          size="sm"
          onClick={handleSave}
          disabled={!isDirty || isLoading || mutation.isPending}
        >
          {mutation.isPending ? "Сохраняем…" : "Сохранить AI-настройки"}
        </Button>
      </div>
    </div>
  );
}
