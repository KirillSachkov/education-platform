"use client";

import { type AiUsageRow, materialProcessingQueryOptions } from "@/entities/material-processing";
import { getErrorMessage } from "@/shared/api";
import { Button } from "@/shared/ui/kit/button";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/shared/ui/kit/table";
import { useQuery } from "@tanstack/react-query";
import { Loader2 } from "lucide-react";
import { useState } from "react";

// Approximate ₽-стоимости моделей. Это _оценка_ для админ-панели — реальная цена
// бьётся в Polza-кабинете и в OTel метриках (`ai_input_tokens_total`,
// `ai_transcription_audio_bytes_total`). Здесь — чтобы автор сразу видел
// «сколько примерно сожгли в ₽» без проваливания в Grafana.
const MODEL_RATES: Record<string, { inputPer1K: number; outputPer1K: number }> = {
  "openai/gpt-4.1-mini": { inputPer1K: 0.0364, outputPer1K: 0.1457 },
  "openai/gpt-4.1-nano": { inputPer1K: 0.0091, outputPer1K: 0.0364 },
  "openai/gpt-4.1": { inputPer1K: 0.182, outputPer1K: 0.728 },
  "deepseek/deepseek-chat": { inputPer1K: 0.0282, outputPer1K: 0.1126 },
  "anthropic/claude-haiku-4.5": { inputPer1K: 0.073, outputPer1K: 0.364 },
  "<default>": { inputPer1K: 0.0364, outputPer1K: 0.1457 }, // = gpt-4.1-mini
};

const TOKENS_PER_JOB_ESTIMATE = {
  TIMECODES: { input: 6_000, output: 800 },
  CONTENT: { input: 8_000, output: 4_500 },
} as const;

const RANGE_OPTIONS = [
  { value: 1, label: "1 день" },
  { value: 7, label: "7 дней" },
  { value: 30, label: "30 дней" },
  { value: 90, label: "90 дней" },
];

export function AdminAiUsagePage() {
  const [days, setDays] = useState(7);
  const { data, isLoading, error } = useQuery(materialProcessingQueryOptions.aiUsage(days));

  const rows: AiUsageRow[] = data?.rows ?? [];
  const summary = summarize(rows);

  return (
    <div className="space-y-4 p-4 md:p-6">
      <header className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <h1 className="text-2xl font-semibold">AI usage / cost</h1>
          <p className="text-sm text-muted-foreground">
            Агрегаты по material-processing pipeline&rsquo;у. Цены приблизительные — точные значения
            в Grafana (метрика <code>ai_input_tokens_total</code>) и в кабинете Polza.
          </p>
        </div>
        <div className="flex gap-1">
          {RANGE_OPTIONS.map((opt) => (
            <Button
              key={opt.value}
              variant={days === opt.value ? "default" : "outline"}
              size="sm"
              onClick={() => setDays(opt.value)}
            >
              {opt.label}
            </Button>
          ))}
        </div>
      </header>

      <section className="grid grid-cols-2 gap-3 md:grid-cols-4">
        <Kpi label="Транскрипций создано" value={data?.transcriptsCreated ?? 0} />
        <Kpi label="Тайм-код job'ов" value={summary.timecodeCount} />
        <Kpi label="Конспект job'ов" value={summary.contentCount} />
        <Kpi label="Прим. стоимость, ₽" value={summary.estimatedCostRub.toFixed(2)} />
      </section>

      {isLoading && (
        <div className="flex items-center gap-2 text-sm text-muted-foreground">
          <Loader2 className="size-4 animate-spin" /> Загружаем…
        </div>
      )}
      {error && (
        <p className="text-sm text-destructive">
          {getErrorMessage(error, "Не удалось загрузить статистику")}
        </p>
      )}

      {!isLoading && rows.length === 0 && !error && (
        <p className="text-sm text-muted-foreground">
          За выбранный период не было запусков AI-pipeline&rsquo;а.
        </p>
      )}

      {rows.length > 0 && (
        <div className="overflow-x-auto">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Дата</TableHead>
                <TableHead>Тип</TableHead>
                <TableHead>Модель</TableHead>
                <TableHead className="hidden sm:table-cell">Статус</TableHead>
                <TableHead className="text-right">Кол-во</TableHead>
                <TableHead className="text-right">Прим. ₽</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {rows.map((row) => (
                <TableRow key={`${row.day}|${row.jobKind}|${row.model}|${row.status}`}>
                  <TableCell className="font-mono text-xs">{row.day}</TableCell>
                  <TableCell>{row.jobKind}</TableCell>
                  <TableCell className="font-mono text-xs">{row.model}</TableCell>
                  <TableCell className="hidden sm:table-cell">{row.status}</TableCell>
                  <TableCell className="text-right">{row.count}</TableCell>
                  <TableCell className="text-right">{estimateRowCost(row).toFixed(2)}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      )}
    </div>
  );
}

function Kpi({ label, value }: { label: string; value: number | string }) {
  return (
    <div className="rounded-lg border bg-card p-3">
      <div className="text-xs uppercase text-muted-foreground">{label}</div>
      <div className="mt-1 text-2xl font-semibold">{value}</div>
    </div>
  );
}

function summarize(rows: AiUsageRow[]) {
  let timecodeCount = 0;
  let contentCount = 0;
  let estimatedCostRub = 0;
  for (const row of rows) {
    if (row.status !== "COMPLETED") continue;
    if (row.jobKind === "TIMECODES") timecodeCount += row.count;
    else if (row.jobKind === "CONTENT") contentCount += row.count;
    estimatedCostRub += estimateRowCost(row);
  }
  return { timecodeCount, contentCount, estimatedCostRub };
}

function estimateRowCost(row: AiUsageRow): number {
  if (row.status !== "COMPLETED") return 0;
  const rate = MODEL_RATES[row.model] ?? MODEL_RATES["<default>"];
  const tokens = TOKENS_PER_JOB_ESTIMATE[row.jobKind];
  const perJob =
    (tokens.input / 1000) * rate.inputPer1K + (tokens.output / 1000) * rate.outputPer1K;
  return perJob * row.count;
}
