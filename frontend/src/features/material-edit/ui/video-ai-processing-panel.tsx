"use client";

import {
  materialProcessingApi,
  materialProcessingQueryOptions,
} from "@/entities/material-processing";
import { invalidateMaterials } from "@/entities/material";
import { videoApi, videoChaptersQueryOptions, type UpdateVideoChapterItem } from "@/entities/video";
import { getErrorMessage, isEnvelopeError } from "@/shared/api";
import { useRoles } from "@/shared/auth/use-roles";
import { Icons } from "@/shared/ui/icons";
import { Button } from "@/shared/ui/kit/button";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/shared/ui/kit/select";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/shared/ui/kit/tabs";
import { DeleteConfirmDialog } from "@/shared/ui/components/delete-confirm-dialog";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useRef, useState } from "react";
import { toast } from "sonner";
import { hasActiveVideoProcessing, isProcessingActive } from "../lib/video-processing-ui";
import { TimecodesEditor } from "./timecodes-editor";
import { VideoProcessingTopBar } from "./video-processing-top-bar";

const DEFAULT_MODEL_VALUE = "__default__";

// Whitelist моделей для admin model-override dropdown'а на dev. Не делать
// freeform input — администратор может опечататься и потерять цикл генерации
// (Polza вернёт ошибку только во время handler'а).
// gpt-4o-mini-transcribe (default) — дешевле, требует `chunking_strategy:auto`
// на audio >30s (передаётся клиентом автоматически). Возвращает только text;
// timestamps синтезируются нормализатором по предложениям.
// gpt-4o-transcribe — точнее на специфичной лексике, в 2× дороже.
// AITunnel модели — без provider-префиксов (см. https://api.aitunnel.ru/public/aitunnel/models/transcriptions).
const STT_MODELS = [
  { id: "gpt-4o-mini-transcribe", label: "gpt-4o-mini-transcribe (дешевле)" },
  { id: "gpt-4o-transcribe", label: "gpt-4o-transcribe (точнее)" },
  { id: "whisper-1", label: "whisper-1 (native segment timestamps)" },
  { id: "gpt-4o-transcribe-diarize", label: "gpt-4o-transcribe-diarize (с разделением спикеров)" },
];

// AITunnel модели — без provider-префиксов (см. https://api.aitunnel.ru/public/aitunnel/models/chat).
const LLM_MODELS = [
  { id: "gpt-5-nano", label: "gpt-5-nano (тайм-коды по умолчанию)" },
  { id: "gpt-5-mini", label: "gpt-5-mini (конспект по умолчанию)" },
  { id: "gpt-4.1-mini", label: "gpt-4.1-mini" },
  { id: "gpt-4.1-nano", label: "gpt-4.1-nano (дешевле)" },
  { id: "gpt-4.1", label: "gpt-4.1 (premium)" },
  { id: "claude-haiku-4.5", label: "claude-haiku-4.5" },
  { id: "deepseek-chat", label: "deepseek-chat" },
];

interface VideoAiProcessingPanelProps {
  videoId: string;
  materialId: string | null;
  /** When true, the panel is rendered inside the material edit form. */
  insideForm?: boolean;
  /**
   * Текущий markdown-контент материала (или пустая строка). Используется
   * чтобы кнопка «Сгенерировать конспект» переключилась в «Перегенерировать»
   * — иначе после refresh страницы кнопка снова показывает «Сгенерировать»,
   * хотя конспект уже есть в теле материала.
   */
  hasMaterialContent?: boolean;
}

/**
 * AI-pipeline UI для VIDEO материалов: транскрипт → тайм-коды → конспект → .srt.
 * Появляется только при наличии прикреплённого видео.
 *
 * Polling unified (#154): page-local query НЕ имеет собственного refetchInterval'а.
 * Единственный источник правды для AI-job state — глобальный `activeJobs`
 * tracker (5s polling, отключается в idle). Page подписывается на ту же cache
 * key (TanStack дедуплицирует subscribers) и инвалидирует свой rich-status
 * query при изменении набора active jobs для этого `videoId`. Это убирает
 * двойной polling (3s + 5s) и race-condition между toast'ами page и tracker'а.
 *
 * Toast'ы на completion — единственный источник: `useAiJobsToasts` в global
 * tracker. Page useEffect'ы делают только cache invalidation (material body,
 * chapters), без toast'ов.
 *
 * Конспект (generateContent) требует materialId — в create-mode (материал
 * ещё не сохранён) кнопка скрыта; автор сохраняет материал, потом запускает.
 */
export function VideoAiProcessingPanel({
  videoId,
  materialId,
  insideForm = true,
  hasMaterialContent = false,
}: VideoAiProcessingPanelProps) {
  const queryClient = useQueryClient();
  const { hasRole } = useRoles();
  const isAdmin = hasRole("platform-admin") || hasRole("platform-owner");

  // Admin-only: позволяет на dev переключать модели per-job без code-change.
  // Передаётся в API как ?modelOverride=...; backend проверяет caller.IsAdmin.
  const [sttModelOverride, setSttModelOverride] = useState<string | undefined>(undefined);
  const [llmModelOverride, setLlmModelOverride] = useState<string | undefined>(undefined);

  // Page-local rich status (transcriptPreparation / generation / activeContentGeneration).
  // НЕТ собственного polling'а — обновления приходят через инвалидацию ниже,
  // которая срабатывает на изменения global activeJobs tracker'а.
  const statusQuery = useQuery({
    ...materialProcessingQueryOptions.video(videoId),
  });

  // Подписываемся на global tracker — единственный источник «есть ли активная
  // обработка»; TanStack дедуплицирует с уже-mounted `AiJobsTracker` widget.
  const activeJobsQuery = useQuery({
    ...materialProcessingQueryOptions.activeJobs(),
  });
  const activeJobs = activeJobsQuery.data?.jobs ?? [];

  // На каждое изменение «slice'а active jobs для этого видео» (job появился,
  // исчез, или сменил status/stage) — инвалидируем rich-status query,
  // чтобы page подхватил свежий progress/stage/error. Дешевле чем держать
  // отдельный 3s timer: запрос идёт только когда global tracker уже доказал
  // что что-то изменилось.
  //
  // Dep — `dataUpdatedAt` глобального кэша (stable timestamp от TanStack), а не
  // derived array. Без этого useEffect запускался бы на каждый рендер (filter
  // даёт новый reference) — мы бы внутри ловили noop через ref-diff, но всё
  // равно платили cost запуска effect'а на каждый render.
  const lastActiveJobsKeyRef = useRef<string>("");
  const activeJobsUpdatedAt = activeJobsQuery.dataUpdatedAt;
  useEffect(() => {
    const key = activeJobs
      .filter((j) => j.videoAssetId === videoId)
      .map((j) => `${j.jobId}:${j.status}:${j.stage}:${j.progressPercent}`)
      .sort()
      .join("|");
    if (key === lastActiveJobsKeyRef.current) return;
    lastActiveJobsKeyRef.current = key;
    queryClient.invalidateQueries({
      queryKey: [materialProcessingQueryOptions.baseKey, "video", videoId],
    });
    // activeJobs нужен для актуального snapshot'а внутри effect'а, но
    // переотрисовка ловится через dataUpdatedAt (stable timestamp).
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [activeJobsUpdatedAt, queryClient, videoId]);

  const chaptersQuery = useQuery({
    ...videoChaptersQueryOptions(videoId),
  });

  const status = statusQuery.data;
  const chapters = chaptersQuery.data?.chapters ?? [];

  const generateTimecodes = useMutation({
    mutationFn: () =>
      materialProcessingApi.generateTimecodes(videoId, {
        modelOverride: llmModelOverride,
      }),
    onSuccess: () => {
      toast.success("Генерация тайм-кодов запущена");
      queryClient.invalidateQueries({
        queryKey: [materialProcessingQueryOptions.baseKey, "video", videoId],
      });
    },
    onError: (error) =>
      toast.error(getErrorMessage(error, "Не удалось запустить генерацию тайм-кодов")),
  });

  const generateContent = useMutation({
    mutationFn: ({ forceOverwrite }: { forceOverwrite: boolean }) => {
      if (!materialId) {
        throw new Error("Материал ещё не сохранён");
      }
      return materialProcessingApi.generateContent({
        videoId,
        request: {
          materialId,
          forceOverwrite,
          modelOverride: llmModelOverride,
        },
      });
    },
    onSuccess: () => {
      toast.success(
        "Генерация конспекта запущена. По завершении тело материала будет перезаписано.",
      );
      queryClient.invalidateQueries({
        queryKey: [materialProcessingQueryOptions.baseKey, "video", videoId],
      });
    },
    onError: (error) => {
      // 409 «у материала уже есть конспект» — ожидаемая ветвь, обрабатывается
      // через AlertDialog в handleGenerateContent. Не показываем toast'ом
      // как ошибку — иначе юзер видит toast «уже есть» ПЕРЕД диалогом.
      // Детектим по error.code (стабильный API-контракт), не по message
      // (локализованный текст можно случайно поменять — баг проявится молча).
      if (isContentExistsError(error)) return;
      toast.error(getErrorMessage(error, "Не удалось запустить генерацию конспекта"));
    },
  });

  // Backend возвращает 409 material.content.exists если конспект непустой.
  // Открываем кастомный AlertDialog → на confirm повторный вызов с
  // forceOverwrite=true. Раньше был window.confirm — выглядел как браузерный
  // alert и выбивался из стиля платформы.
  const [overwriteDialogOpen, setOverwriteDialogOpen] = useState(false);

  const handleGenerateContent = async () => {
    try {
      await generateContent.mutateAsync({ forceOverwrite: false });
    } catch (error) {
      if (isContentExistsError(error)) {
        setOverwriteDialogOpen(true);
      }
      // Остальные error'ы уже обработаны в mutation onError → toast.
    }
  };

  const confirmOverwriteContent = async () => {
    try {
      await generateContent.mutateAsync({ forceOverwrite: true });
      setOverwriteDialogOpen(false);
    } catch {
      // Ошибки surface'ятся через toast в onError mutation; диалог оставляем
      // открытым чтобы юзер мог попробовать ещё раз или закрыть вручную.
    }
  };

  const saveChapters = useMutation({
    mutationFn: (items: UpdateVideoChapterItem[]) =>
      videoApi.replaceChapters({
        videoId,
        request: {
          chapters: items.map((item) => ({
            id: item.id,
            title: item.title,
            startSeconds: item.startSeconds,
            sortOrder: item.sortOrder,
          })),
        },
      }),
    onSuccess: () => {
      toast.success("Главы сохранены в Kinescope");
      queryClient.invalidateQueries({ queryKey: ["videos", "chapters", videoId] });
    },
    onError: (error) => toast.error(getErrorMessage(error, "Не удалось сохранить главы")),
  });

  // На completion content/timecode job'а — инвалидируем зависимые queries,
  // чтобы UI подхватил свежий markdown / chapters без hard refresh.
  // Toast'ы НЕ показываем здесь: единственный источник completion toast'а —
  // `useAiJobsToasts` в global `AiJobsTracker`. Без этого ограничения юзер
  // видел дубль toast'а на completion (один из page, один из tracker'а).
  //
  // Это true side-effect (Category E из useEffect-audit): подписка на изменения
  // poll-данных, побочное действие в form/parent (invalidate query). Без useEffect
  // не выразимо чисто — нет «event» в TanStack Query кроме query cache subscribe.
  const lastContentStatusRef = useRef<string | null | undefined>(
    status?.activeContentGeneration?.status,
  );
  useEffect(() => {
    const previous = lastContentStatusRef.current;
    const current = status?.activeContentGeneration?.status;
    lastContentStatusRef.current = current;

    const wasActive = previous === "QUEUED" || previous === "PROCESSING";
    if (!wasActive) return;

    if (current === "COMPLETED" || current === undefined || current === null) {
      if (materialId) {
        invalidateMaterials(queryClient);
        queryClient.invalidateQueries({
          queryKey: ["materials", materialId, "detail"],
        });
      }
    }
  }, [status?.activeContentGeneration?.status, materialId, queryClient]);

  // Аналогично для timecode-job: на completion инвалидируем
  // videoChaptersQueryOptions чтобы chapters editor подхватил свежие главы.
  const lastTimecodeStatusRef = useRef<string | null | undefined>(status?.generation?.status);
  useEffect(() => {
    const previous = lastTimecodeStatusRef.current;
    const current = status?.generation?.status;
    lastTimecodeStatusRef.current = current;

    const wasActive = previous === "QUEUED" || previous === "PROCESSING";
    if (!wasActive) return;

    if (current === "COMPLETED" || current === undefined || current === null) {
      queryClient.invalidateQueries({ queryKey: ["videos", "chapters", videoId] });
    }
  }, [status?.generation?.status, videoId, queryClient]);

  const isGenerating = generateTimecodes.isPending || generateContent.isPending;
  const isAnyJobActive = hasActiveVideoProcessing(status);

  // Per-step state: «не создан» | «в процессе» | «готов» — отображается inline
  // pill-ами над кнопками, чтобы юзер видел что уже сделано и что осталось,
  // даже когда progress bar уже скрылся (job завершён).
  //
  // Транскрипт и тайм-коды используют ОДНУ backend-таблицу
  // (`timecode_generation_jobs`) с дискриминатором `mode`. Allowlist по mode
  // (а не отрицание TRANSCRIPT_ONLY): если backend в будущем добавит новый mode
  // (`CONTENT_DRAFT` etc.), пилюля «Тайм-коды» ошибочно не будет крутить спиннер
  // на чужой mode.
  const generationActive =
    status?.generation?.status !== undefined && isProcessingActive(status.generation.status);
  const generationMode = status?.generation?.mode;
  const isTimecodesJob = generationMode === "TIMECODES";

  const timecodesInProgress = generateTimecodes.isPending || (generationActive && isTimecodesJob);
  const timecodesState: StepState = timecodesInProgress
    ? "in-progress"
    : chapters.length > 0
      ? "done"
      : "none";

  const contentInProgress =
    generateContent.isPending || isProcessingActive(status?.activeContentGeneration?.status);
  // «Готов» — если в последнюю поллинг-сессию activeContentGeneration был
  // COMPLETED ИЛИ если material body уже содержит markdown (parent передал
  // через hasMaterialContent). Второй путь нужен после refresh страницы:
  // active-job на тот момент уже null, но содержимое в БД есть.
  const lastContentStatus = status?.activeContentGeneration?.status;
  const contentState: StepState = contentInProgress
    ? "in-progress"
    : lastContentStatus === "COMPLETED" || hasMaterialContent
      ? "done"
      : "none";

  // Кнопки действий блокируются если есть активный job (предотвратить одновременные
  // enqueue, хотя backend и сам идемпотентен — UX чище).
  const actionsDisabled = isGenerating || isAnyJobActive;

  return (
    <section
      className={
        insideForm ? "space-y-3 rounded-lg border border-border/50 bg-card/40 p-4" : "space-y-3"
      }
    >
      <header className="flex items-center justify-between gap-3">
        <div className="flex items-center gap-2">
          <Icons.ai size={15} className="text-muted-foreground" />
          <span className="text-sm font-medium">AI-обработка видео</span>
        </div>
      </header>

      <VideoProcessingTopBar
        status={status}
        isFetching={statusQuery.isFetching}
        isGeneratingTimecodes={generateTimecodes.isPending}
        isGeneratingContent={generateContent.isPending}
      />

      <Tabs defaultValue="timecodes" className="w-full">
        <TabsList className="grid w-full max-w-full grid-cols-2">
          <TabsTrigger value="timecodes" className="min-h-[44px] gap-1.5 sm:min-h-0">
            <Icons.list size={13} />
            Тайм-коды
            <StepDot state={timecodesState} />
          </TabsTrigger>
          <TabsTrigger value="content" className="min-h-[44px] gap-1.5 sm:min-h-0">
            <Icons.document size={13} />
            Конспект
            <StepDot state={contentState} />
          </TabsTrigger>
        </TabsList>

        <TabsContent value="timecodes" className="space-y-3 pt-3">
          <p className="text-xs text-muted-foreground">
            AI делит видео на главы по содержанию транскрипта. Главы пишутся в Kinescope — зрители
            увидят разделы прямо в плеере.
          </p>
          {chapters.length > 0 ? (
            // Глав уже сгенерированы — TimecodesEditor имеет свою кнопку
            // регенерации в шапке (wand-иконка), большая дублирующая кнопка
            // не нужна.
            <TimecodesEditor
              timecodes={chapters}
              isSaving={saveChapters.isPending}
              isGenerating={generateTimecodes.isPending}
              canGenerate={!actionsDisabled}
              generateTooltip="Перегенерировать главы через AI"
              onGenerate={() => generateTimecodes.mutate()}
              onSave={async (items) => {
                await saveChapters.mutateAsync(items);
              }}
            />
          ) : (
            <div className="flex flex-wrap gap-2">
              <Button
                type="button"
                variant="outline"
                size="sm"
                disabled={actionsDisabled}
                onClick={() => generateTimecodes.mutate()}
              >
                <Icons.list size={14} />
                Сгенерировать тайм-коды
              </Button>
            </div>
          )}
        </TabsContent>

        <TabsContent value="content" className="space-y-3 pt-3">
          <p className="text-xs text-muted-foreground">
            AI пишет markdown-конспект урока в тело материала: ключевые термины, шаги, правила,
            примеры кода. Перезапишет существующее тело — будет запрос подтверждения.
          </p>
          <div className="flex flex-wrap gap-2">
            <Button
              type="button"
              variant="outline"
              size="sm"
              disabled={actionsDisabled || !materialId}
              title={
                !materialId
                  ? "Сначала сохраните материал — backend пишет конспект в его тело"
                  : undefined
              }
              onClick={handleGenerateContent}
            >
              <Icons.document size={14} />
              {contentState === "done" ? "Перегенерировать конспект" : "Сгенерировать конспект"}
            </Button>
          </div>
        </TabsContent>
      </Tabs>

      {isAdmin && (
        <details className="group rounded-lg border border-border/40 bg-muted/20">
          <summary className="flex cursor-pointer items-center gap-2 px-3 py-2 text-xs text-muted-foreground hover:text-foreground">
            <Icons.shield size={12} />
            <span>Расширенные настройки моделей</span>
            <Icons.chevronDown
              size={12}
              className="ml-auto transition-transform group-open:rotate-180"
            />
          </summary>
          <div className="space-y-3 border-t border-border/40 px-3 py-3">
            <p className="text-xs text-muted-foreground">
              Переопределить STT/LLM-модели только для следующего запуска. Без override используются
              дефолты из настроек платформы.
            </p>
            <div className="grid gap-3 sm:grid-cols-2">
              <div className="space-y-1.5">
                <label htmlFor="ai-stt-model" className="text-xs font-medium">
                  STT — распознавание речи
                </label>
                <Select
                  value={sttModelOverride ?? DEFAULT_MODEL_VALUE}
                  onValueChange={(v) =>
                    setSttModelOverride(v === DEFAULT_MODEL_VALUE ? undefined : v)
                  }
                  disabled={actionsDisabled}
                >
                  <SelectTrigger id="ai-stt-model" size="sm" className="w-full">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value={DEFAULT_MODEL_VALUE}>
                      по умолчанию (из настроек платформы)
                    </SelectItem>
                    {STT_MODELS.map((m) => (
                      <SelectItem key={m.id} value={m.id}>
                        {m.label}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
              <div className="space-y-1.5">
                <label htmlFor="ai-llm-model" className="text-xs font-medium">
                  LLM — тайм-коды и конспект
                </label>
                <Select
                  value={llmModelOverride ?? DEFAULT_MODEL_VALUE}
                  onValueChange={(v) =>
                    setLlmModelOverride(v === DEFAULT_MODEL_VALUE ? undefined : v)
                  }
                  disabled={actionsDisabled}
                >
                  <SelectTrigger id="ai-llm-model" size="sm" className="w-full">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value={DEFAULT_MODEL_VALUE}>
                      по умолчанию (из настроек платформы)
                    </SelectItem>
                    {LLM_MODELS.map((m) => (
                      <SelectItem key={m.id} value={m.id}>
                        {m.label}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
            </div>
          </div>
        </details>
      )}

      <DeleteConfirmDialog
        open={overwriteDialogOpen}
        onOpenChange={setOverwriteDialogOpen}
        title="Перезаписать конспект?"
        description={
          <>
            У материала уже есть текст в теле. AI перезапишет его новым конспектом по транскрипту
            видео. Текущий текст будет потерян.
          </>
        }
        confirmLabel="Перезаписать"
        cancelLabel="Отмена"
        onConfirm={confirmOverwriteContent}
        isPending={generateContent.isPending}
      />
    </section>
  );
}

/** Backend возвращает 409 с code=`material.content.exists` если у материала
 *  уже есть текст в теле и автор пытается сгенерировать конспект. Детектим
 *  по стабильному error code, не по локализованному message. */
function isContentExistsError(error: unknown): boolean {
  return isEnvelopeError(error) && error.messages.some((m) => m.code === "material.content.exists");
}

type StepState = "none" | "in-progress" | "done";

function StepDot({ state }: { state: StepState }) {
  if (state === "in-progress") {
    return <Icons.loading size={11} className="animate-spin text-primary" />;
  }
  if (state === "done") {
    return (
      <span className="size-1.5 rounded-full bg-emerald-500" title="Готов" aria-label="Готов" />
    );
  }
  return (
    <span
      className="size-1.5 rounded-full bg-muted-foreground/30"
      title="Не создан"
      aria-label="Не создан"
    />
  );
}
