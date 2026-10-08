"use client";

import {
  type AiModelSettingsDto,
  type AiModelSlotDto,
  materialProcessingApi,
  materialProcessingQueryOptions,
} from "@/entities/material-processing";
import { getErrorMessage } from "@/shared/api";
import { Button } from "@/shared/ui/kit/button";
import { Input } from "@/shared/ui/kit/input";
import { Switch } from "@/shared/ui/kit/switch";
import { Icons } from "@/shared/ui/icons";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { toast } from "sonner";

type SlotKey = "speechToText" | "timecodeGeneration" | "contentGeneration";

const SLOT_LABELS: Record<SlotKey, { title: string; description: string }> = {
  speechToText: {
    title: "Транскрипция (STT)",
    description:
      "Распознавание речи из аудио. По умолчанию gpt-4o-mini-transcribe (~0.27 ₽/мин). whisper-1 — стабильнее, отдаёт нативные сегменты с timestamps (~0.55 ₽/мин). Цена слабо зависит от модели — большая часть стоимости в audio-секундах.",
  },
  timecodeGeneration: {
    title: "Тайм-коды",
    description:
      "LLM строит JSON со списком глав по транскрипту. Дефолт — gpt-4.1-nano (issue #314): для JSON-листа глав точности достаточно, цена в 3-5× ниже gpt-4.1-mini. Для агрессивной экономии — deepseek-v4-pro (~₽167/1M output).",
  },
  contentGeneration: {
    title: "Конспект (markdown в тело материала)",
    description:
      "LLM пишет markdown-конспект по транскрипту. Длинные видео гонят chunk + merge → 2-6 calls на материал. Дефолт — gpt-4.1-mini (качество markdown важнее цены). Бюджетная альтернатива — deepseek-v4-pro (~17× дешевле output), агрессивный downgrade — gpt-4.1-nano (короче, схематичнее). Качество — gpt-4.1 / gpt-5 / claude-sonnet-4.5 / gemini-2.5-pro.",
  },
};

// AITunnel модели — без provider-префиксов (см. https://api.aitunnel.ru/public/aitunnel/models).
// Порядок в массиве = порядок suggestion'ов в datalist'е. Первая — самая
// дешёвая/рекомендуемая для слота, дальше — по возрастанию цены и качества.
// Список можно править свободно: бэк принимает любую строку и передаёт в
// AI-провайдер; если модели нет в подписке у вашего ключа, AI-провайдер вернёт
// 4xx и job упадёт с ai.provider.failed (см. сообщение в `/admin/ai-usage`).
// Полный актуальный список доступных моделей вашего ключа — в Личном кабинете
// AITunnel → «Модели».
const SLOT_MODEL_PRESETS: Record<SlotKey, readonly string[]> = {
  speechToText: [
    "gpt-4o-mini-transcribe",
    "gpt-4o-transcribe",
    "whisper-1",
    "gpt-4o-transcribe-diarize",
  ],
  timecodeGeneration: [
    "gpt-4.1-nano",
    "gpt-4.1-mini",
    "gpt-4.1",
    "gpt-5-nano",
    "gpt-5-mini",
    "deepseek-v4-pro",
    "claude-haiku-4.5",
    "claude-sonnet-4.5",
    "gemini-2.5-flash-lite",
    "gemini-2.5-flash",
    "gemini-2.5-pro",
    "deepseek-chat",
  ],
  contentGeneration: [
    "gpt-4.1-nano",
    "gpt-4.1-mini",
    "gpt-4.1",
    "gpt-5-nano",
    "gpt-5-mini",
    "gpt-5",
    "deepseek-v4-pro",
    "claude-haiku-4.5",
    "claude-sonnet-4.5",
    "gemini-2.5-flash-lite",
    "gemini-2.5-flash",
    "gemini-2.5-pro",
    "deepseek-chat",
  ],
};

const SLOT_ORDER: SlotKey[] = ["speechToText", "timecodeGeneration", "contentGeneration"];

interface SlotFormState {
  model: string;
  temperature: string;
  maxOutputTokens: string;
  timeoutSeconds: string;
}

interface FormState {
  slots: Record<SlotKey, SlotFormState>;
  autoProcessVideosEnabled: boolean;
}

function dtoToForm(dto: AiModelSettingsDto): FormState {
  const map = (slot: AiModelSlotDto): SlotFormState => ({
    model: slot.model,
    temperature: slot.temperature?.toString() ?? "",
    maxOutputTokens: slot.maxOutputTokens?.toString() ?? "",
    timeoutSeconds: slot.timeoutSeconds?.toString() ?? "",
  });
  return {
    slots: {
      speechToText: map(dto.speechToText),
      timecodeGeneration: map(dto.timecodeGeneration),
      contentGeneration: map(dto.contentGeneration),
    },
    autoProcessVideosEnabled: dto.autoProcessVideosEnabled,
  };
}

function parseSlot(form: SlotFormState): AiModelSlotDto {
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

export function AdminAiModelsPage() {
  const queryClient = useQueryClient();
  const { data, isLoading, error } = useQuery(
    materialProcessingQueryOptions.aiModelSettings(),
  );

  // Derived state: пока юзер не редактировал, рендерим прямо из data.
  // При первом изменении кладём snapshot в overrides, дальше обновляем его.
  // Сброс через `Отменить изменения` = `setOverrides(null)` → форма снова берётся из data.
  const [overrides, setOverrides] = useState<FormState | null>(null);
  const form: FormState | null = overrides ?? (data ? dtoToForm(data) : null);

  const mutation = useMutation({
    mutationFn: () => {
      if (!form) throw new Error("form not ready");
      return materialProcessingApi.updateAiModelSettings({
        speechToText: parseSlot(form.slots.speechToText),
        timecodeGeneration: parseSlot(form.slots.timecodeGeneration),
        contentGeneration: parseSlot(form.slots.contentGeneration),
        autoProcessVideosEnabled: form.autoProcessVideosEnabled,
      });
    },
    onSuccess: (saved) => {
      toast.success("Настройки моделей обновлены");
      queryClient.setQueryData(
        materialProcessingQueryOptions.aiModelSettings().queryKey,
        saved,
      );
      setOverrides(null);
    },
    onError: (err) =>
      toast.error(getErrorMessage(err, "Ошибка сохранения настроек моделей")),
  });

  const updateField = (slot: SlotKey, field: keyof SlotFormState, value: string) => {
    setOverrides((current) => {
      const base = current ?? form;
      if (!base) return current;
      return {
        ...base,
        slots: { ...base.slots, [slot]: { ...base.slots[slot], [field]: value } },
      };
    });
  };

  const setAutoProcess = (enabled: boolean) => {
    setOverrides((current) => {
      const base = current ?? form;
      if (!base) return current;
      return { ...base, autoProcessVideosEnabled: enabled };
    });
  };

  if (isLoading || form === null) {
    return (
      <div className="flex h-64 items-center justify-center">
        <Icons.loading className="h-6 w-6 animate-spin text-muted-foreground" />
      </div>
    );
  }

  if (error) {
    return (
      <div className="rounded-md border border-destructive/40 bg-destructive/5 p-4 text-sm text-destructive">
        {getErrorMessage(error, "Не удалось загрузить настройки моделей")}
      </div>
    );
  }

  return (
    <div className="space-y-6">
      <header className="space-y-2">
        <h1 className="text-2xl font-semibold tracking-tight">AI-модели для видео-pipeline&rsquo;а</h1>
        <p className="max-w-3xl text-sm text-muted-foreground">
          Настройки применяются ко всем последующим job&rsquo;ам без рестарта сервиса. Текущие
          активные job&rsquo;ы доезжают на старой модели — отменять их вручную не требуется. Если
          ничего не сохраняли через эту страницу, используются дефолты из{" "}
          <code className="rounded bg-muted px-1 py-0.5 text-xs">appsettings.{`{Environment}`}.json</code>.
        </p>
        {data && (
          <div className="flex flex-wrap items-center gap-2 text-xs text-muted-foreground">
            <span className="rounded-full bg-muted px-2 py-0.5">
              Источник: <strong>{data.source === "DATABASE" ? "БД (override)" : "appsettings (default)"}</strong>
            </span>
            {data.updatedAtUtc && (
              <span>
                Изменено {new Date(data.updatedAtUtc).toLocaleString("ru-RU")}
                {data.updatedByUserId ? ` (user ${data.updatedByUserId.slice(0, 8)})` : ""}
              </span>
            )}
          </div>
        )}
      </header>

      <section
        className="flex items-center justify-between gap-4 rounded-lg border bg-card p-5 shadow-sm"
        aria-labelledby="auto-process-title"
      >
        <div className="min-w-0 space-y-1">
          <h2 id="auto-process-title" className="text-base font-medium">
            Авто-обработка видео при загрузке
          </h2>
          <p className="text-xs text-muted-foreground">
            Автоматически запускать транскрипцию и тайм-коды, когда загруженное видео готово.
          </p>
        </div>
        <Switch
          checked={form.autoProcessVideosEnabled}
          disabled={mutation.isPending}
          onCheckedChange={setAutoProcess}
          aria-labelledby="auto-process-title"
        />
      </section>

      <div className="grid gap-4">
        {SLOT_ORDER.map((slot) => (
          <section
            key={slot}
            className="rounded-lg border bg-card p-5 shadow-sm"
            aria-labelledby={`slot-${slot}-title`}
          >
            <div className="mb-4 space-y-1">
              <h2
                id={`slot-${slot}-title`}
                className="text-base font-medium"
              >
                {SLOT_LABELS[slot].title}
              </h2>
              <p className="text-xs text-muted-foreground">{SLOT_LABELS[slot].description}</p>
            </div>
            <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-4">
              <Field
                id={`${slot}-model`}
                label="Модель"
                value={form.slots[slot].model}
                placeholder={SLOT_MODEL_PRESETS[slot][0]}
                listId={`${slot}-model-presets`}
                presets={SLOT_MODEL_PRESETS[slot]}
                onChange={(v) => updateField(slot, "model", v)}
              />
              <Field
                id={`${slot}-temperature`}
                label="Temperature (0–2)"
                value={form.slots[slot].temperature}
                placeholder="0"
                inputMode="decimal"
                onChange={(v) => updateField(slot, "temperature", v)}
              />
              <Field
                id={`${slot}-max-output-tokens`}
                label="Max output tokens"
                value={form.slots[slot].maxOutputTokens}
                placeholder="4000"
                inputMode="numeric"
                onChange={(v) => updateField(slot, "maxOutputTokens", v)}
              />
              <Field
                id={`${slot}-timeout-seconds`}
                label="Timeout, секунд"
                value={form.slots[slot].timeoutSeconds}
                placeholder="300"
                inputMode="numeric"
                onChange={(v) => updateField(slot, "timeoutSeconds", v)}
              />
            </div>
          </section>
        ))}
      </div>

      <footer className="flex items-center justify-end gap-2 border-t pt-4">
        <Button
          variant="outline"
          type="button"
          disabled={mutation.isPending || overrides === null}
          onClick={() => setOverrides(null)}
        >
          Отменить изменения
        </Button>
        <Button
          type="button"
          disabled={mutation.isPending}
          onClick={() => mutation.mutate()}
        >
          {mutation.isPending && <Icons.loading className="mr-2 h-4 w-4 animate-spin" />}
          Сохранить
        </Button>
      </footer>
    </div>
  );
}

function Field({
  id,
  label,
  value,
  placeholder,
  onChange,
  inputMode,
  listId,
  presets,
}: {
  id: string;
  label: string;
  value: string;
  placeholder?: string;
  onChange: (value: string) => void;
  inputMode?: React.HTMLAttributes<HTMLInputElement>["inputMode"];
  listId?: string;
  presets?: readonly string[];
}) {
  return (
    <div className="space-y-1.5">
      <label htmlFor={id} className="text-xs font-medium text-muted-foreground">
        {label}
      </label>
      <Input
        id={id}
        value={value}
        placeholder={placeholder}
        inputMode={inputMode}
        list={listId}
        onChange={(e) => onChange(e.target.value)}
      />
      {listId && presets && presets.length > 0 && (
        <datalist id={listId}>
          {presets.map((preset) => (
            <option key={preset} value={preset} />
          ))}
        </datalist>
      )}
    </div>
  );
}
