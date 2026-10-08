"use client";

import {
  type AssignmentReviewAiSettingsDto,
  type AssignmentReviewAiSlotDto,
  type AssignmentReviewAiSlotInput,
  assignmentReviewAiSettingsApi,
  assignmentReviewAiSettingsQueryOptions,
} from "@/entities/assignment-review-ai-settings";
import { useCancelActiveAiReviews } from "@/entities/ai-review";
import { getErrorMessage } from "@/shared/api";
import { Badge } from "@/shared/ui/kit/badge";
import { Button } from "@/shared/ui/kit/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/ui/kit/card";
import { Input } from "@/shared/ui/kit/input";
import { Switch } from "@/shared/ui/kit/switch";
import { Textarea } from "@/shared/ui/kit/textarea";
import { Icons } from "@/shared/ui/icons";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { toast } from "sonner";

interface SlotFormState {
  model: string;
  temperature: string;
  maxOutputTokens: string;
  timeoutSeconds: string;
  basePrompt: string;
  reviewEnabled: boolean;
  repoContextEnabled: boolean;
}

function dtoToForm(dto: AssignmentReviewAiSettingsDto): SlotFormState {
  return {
    model: dto.reviewer.model,
    temperature: dto.reviewer.temperature?.toString() ?? "",
    maxOutputTokens: dto.reviewer.maxOutputTokens?.toString() ?? "",
    timeoutSeconds: dto.reviewer.timeoutSeconds?.toString() ?? "",
    basePrompt: dto.reviewerBasePrompt.value ?? "",
    reviewEnabled: dto.reviewEnabled,
    repoContextEnabled: dto.repoContextEnabled,
  };
}

function parseSlot(form: SlotFormState): AssignmentReviewAiSlotInput {
  const parseNullableNumber = (value: string): number | null => {
    if (value.trim() === "") return null;
    const parsed = Number(value);
    return Number.isFinite(parsed) ? parsed : null;
  };
  return {
    model: form.model.trim(),
    temperature: parseNullableNumber(form.temperature),
    maxOutputTokens: parseNullableNumber(form.maxOutputTokens),
    timeoutSeconds: parseNullableNumber(form.timeoutSeconds),
  };
}

export function AssignmentReviewAiSection() {
  const queryClient = useQueryClient();
  const { data, isLoading, error } = useQuery(assignmentReviewAiSettingsQueryOptions());
  const [draft, setDraft] = useState<SlotFormState | null>(null);
  const form = draft ?? (data ? dtoToForm(data) : null);
  const cancelActiveMutation = useCancelActiveAiReviews();

  const mutation = useMutation({
    mutationFn: (request: Parameters<typeof assignmentReviewAiSettingsApi.update>[0]) =>
      assignmentReviewAiSettingsApi.update(request),
    onSuccess: () => {
      toast.success("Настройки AI ревью обновлены");
      setDraft(null);
      queryClient.invalidateQueries({
        queryKey: ["assignment-review-ai-settings"],
      });
    },
    onError: (err) => {
      toast.error(getErrorMessage(err, "Ошибка сохранения настроек"));
    },
  });

  if (isLoading) {
    return (
      <Card>
        <CardContent className="flex items-center gap-2 py-6 text-sm text-muted-foreground">
          <Icons.loading className="size-4 animate-spin" /> Загрузка настроек…
        </CardContent>
      </Card>
    );
  }

  if (error || !data || !form) {
    return (
      <Card className="border-red/30">
        <CardContent className="py-6 text-sm text-red">
          Не удалось загрузить настройки AI ревью.
        </CardContent>
      </Card>
    );
  }

  const handleSubmit = () => {
    mutation.mutate({
      reviewer: parseSlot(form),
      reviewerBasePrompt: form.basePrompt.trim() === "" ? null : form.basePrompt.trim(),
      reviewEnabled: form.reviewEnabled,
      repoContextEnabled: form.repoContextEnabled,
    });
  };

  const handleCancelActive = () => {
    const confirmed = window.confirm(
      "Остановить все AI-проверки в статусе «в очереди» и «проверяется»?",
    );
    if (confirmed) cancelActiveMutation.mutate();
  };

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-base">
          <Icons.ai className="size-4 text-primary" /> Assignment Review AI
        </CardTitle>
      </CardHeader>
      <CardContent className="space-y-6">
        <div className="flex items-center justify-between gap-3 rounded-lg border border-border/60 bg-muted/30 p-4">
          <div className="space-y-1">
            <h3 className="text-sm font-semibold">AI-проверка пул-реквестов</h3>
            <p className="text-xs text-muted-foreground">
              Главный тумблер платформы. Когда выключено — новые сабмиты идут сразу на ручное ревью
              автором, AI-проверка не создаётся и не запускается (ни авто, ни вручную). По умолчанию
              выключено.
            </p>
          </div>
          <Switch
            checked={form.reviewEnabled}
            onCheckedChange={(checked) => setDraft({ ...form, reviewEnabled: checked })}
            aria-label="AI-проверка пул-реквестов"
          />
        </div>

        <div className="flex items-center justify-between gap-3 rounded-lg border border-border/60 bg-muted/30 p-4">
          <div className="space-y-1">
            <h3 className="text-sm font-semibold">Дозапрос файлов репозитория</h3>
            <p className="text-xs text-muted-foreground">
              Ревьюер видит карту репозитория и может запросить до 8 файлов вне диффа (максимум 2
              дополнительных обращения к модели), чтобы не гадать про код, которого нет в PR. Дороже
              и дольше на спорных проверках. По умолчанию выключено.
            </p>
          </div>
          <Switch
            checked={form.repoContextEnabled}
            onCheckedChange={(checked) => {
              setDraft({ ...form, repoContextEnabled: checked });
            }}
            aria-label="Дозапрос файлов репозитория"
          />
        </div>

        <div className="flex flex-col gap-3 rounded-lg border border-red/30 bg-red-dim p-4 sm:flex-row sm:items-center sm:justify-between">
          <div className="space-y-1">
            <h3 className="text-sm font-semibold text-red">Аварийная остановка проверок</h3>
            <p className="text-xs text-red/80">
              Остановит текущие AI-проверки в очереди и в работе. История сохранится, студенты уйдут
              на ручную обработку или новый запуск.
            </p>
          </div>
          <Button
            type="button"
            variant="destructive"
            onClick={handleCancelActive}
            disabled={cancelActiveMutation.isPending}
            className="w-full sm:w-auto"
          >
            {cancelActiveMutation.isPending ? (
              <>
                <Icons.loading className="mr-1.5 size-4 animate-spin" />
                Останавливаем…
              </>
            ) : (
              <>
                <Icons.stop className="mr-1.5 size-4" />
                Остановить активные
              </>
            )}
          </Button>
        </div>

        <SlotEditor
          label={{
            title: "Reviewer (LLM-ревью PR)",
            description: "LLM, которая читает diff + контекст и пишет inline-комменты + summary.",
          }}
          slot={data.reviewer}
          form={form}
          onChange={setDraft}
        />

        <div className="space-y-2 border-t border-border/40 pt-4">
          <div className="flex items-center justify-between gap-3">
            <h3 className="text-sm font-semibold">Базовый промпт (для всех задач)</h3>
            <Badge
              variant="outline"
              className={
                data.reviewerBasePrompt.source === "DATABASE"
                  ? "border-blue/30 bg-blue/10 text-blue"
                  : "border-muted-foreground/30 text-muted-foreground"
              }
            >
              {data.reviewerBasePrompt.source}
            </Badge>
          </div>
          <p className="text-xs text-muted-foreground">
            Доверенные инструкции, подмешиваемые в КАЖДУЮ проверку (поверх — промпт задачи и
            гайдлайны проекта). Пусто — сброс к дефолту из конфига.
          </p>
          <Textarea
            rows={5}
            placeholder="Напр.: Если студент оставил вопросы или TODO к ревьюеру в PR — ответь на них."
            value={form.basePrompt}
            onChange={(e) => setDraft({ ...form, basePrompt: e.target.value })}
          />
        </div>

        <div className="flex items-center justify-between border-t border-border/40 pt-4">
          {data.updatedAt ? (
            <p className="text-xs text-muted-foreground">
              Обновлено: {new Date(data.updatedAt).toLocaleString("ru-RU")}
            </p>
          ) : (
            <span />
          )}
          <Button type="button" onClick={handleSubmit} disabled={mutation.isPending}>
            {mutation.isPending ? (
              <>
                <Icons.loading className="mr-1.5 size-4 animate-spin" />
                Сохраняем…
              </>
            ) : (
              "Сохранить"
            )}
          </Button>
        </div>
      </CardContent>
    </Card>
  );
}

interface SlotEditorProps {
  label: { title: string; description: string };
  slot: AssignmentReviewAiSlotDto;
  form: SlotFormState;
  onChange: (next: SlotFormState) => void;
}

function SlotEditor({ label, slot, form, onChange }: SlotEditorProps) {
  return (
    <div className="space-y-2">
      <div className="flex items-center justify-between gap-3">
        <h3 className="text-sm font-semibold">{label.title}</h3>
        <Badge
          variant="outline"
          className={
            slot.source === "DATABASE"
              ? "border-blue/30 bg-blue/10 text-blue"
              : "border-muted-foreground/30 text-muted-foreground"
          }
        >
          {slot.source}
        </Badge>
      </div>
      <p className="text-xs text-muted-foreground">{label.description}</p>
      <div className="grid grid-cols-1 gap-2 sm:grid-cols-4">
        <Input
          placeholder="model id"
          value={form.model}
          onChange={(e) => onChange({ ...form, model: e.target.value })}
        />
        <Input
          placeholder="temperature"
          value={form.temperature}
          onChange={(e) => onChange({ ...form, temperature: e.target.value })}
        />
        <Input
          placeholder="max output tokens"
          value={form.maxOutputTokens}
          onChange={(e) => onChange({ ...form, maxOutputTokens: e.target.value })}
        />
        <Input
          placeholder="timeout (s)"
          value={form.timeoutSeconds}
          onChange={(e) => onChange({ ...form, timeoutSeconds: e.target.value })}
        />
      </div>
    </div>
  );
}
