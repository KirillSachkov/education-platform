"use client";

import {
  getMaterialKindBadge,
  invalidateMaterials,
  type CreateMaterialRequest,
  type MaterialAccessType,
  type MaterialDetailDto,
  type MaterialId,
  type MaterialKind,
  type UpdateMaterialRequest,
} from "@/entities/material";
import { TagsField } from "@/entities/tag";
import { bindMarkdownAssets, useMarkdownFileUpload, useMarkdownImageUpload } from "@/entities/file";
import { EntityTypes } from "@/shared/config/entity-types";
import { cn } from "@/shared/lib/css";
import type { MediaOwnerType, VideoInfo } from "@/shared/types";
import { AccessTypeSelector } from "@/shared/ui/components/access-type-selector";
import { Icons, type IconComponent } from "@/shared/ui/icons";
import { Button } from "@/shared/ui/kit/button";
import { Input } from "@/shared/ui/kit/input";
import { Label } from "@/shared/ui/kit/label";
import { useQueryClient } from "@tanstack/react-query";
import { zodResolver } from "@hookform/resolvers/zod";
import dynamic from "next/dynamic";
import { useRef, useState } from "react";
import { Controller, useForm, type FieldErrors } from "react-hook-form";
import { toast } from "sonner";
import { useUploadMaterialPreview } from "../model/use-upload-material-preview";
import { MaterialPreviewUpload } from "./material-preview-upload";
import { MATERIAL_KINDS, materialFormSchema, type MaterialFormValues } from "../model/schemas";
import { useFormAutosave, type AutosaveStatus } from "../model/use-form-autosave";
import { useMediaAutosave } from "../model/use-media-autosave";
import { VideoAiProcessingPanel } from "./video-ai-processing-panel";

const MarkdownEditor = dynamic(
  () =>
    import("@/shared/ui/kit/markdown-editor").then((m) => ({
      default: m.MarkdownEditor,
    })),
  {
    ssr: false,
    loading: () => <div className="h-64 animate-pulse rounded-md bg-muted" />,
  },
);

type FormContext = "inline" | "full-page";
type FormMode = "create" | "edit";

const kindIcons: Record<MaterialKind, IconComponent> = {
  ARTICLE: Icons.document,
  VIDEO: Icons.play,
  NOTE: Icons.note,
  STREAM: Icons.stream,
};

export interface MaterialFormInitialValue {
  title: string;
  content: string;
  description: string;
  kind: MaterialKind;
  accessType: MaterialAccessType;
}

export interface VideoUploadSlotProps {
  entityId: string | null;
  entityType: MediaOwnerType;
  draftId: string | null;
  video: VideoInfo | null;
  onVideoChange: (hasVideo: boolean) => void;
  onAssetIdChange: (assetId: string | null) => void;
  entityLabel: string;
}

export interface MaterialFormProps {
  mode: FormMode;
  context: FormContext;
  /** Material id (when editing) — used for markdown image binding and form key */
  materialId?: string;
  initialValue?: MaterialFormInitialValue;
  /** Full material detail — required to show existing video/cover state in edit mode */
  material?: MaterialDetailDto | null;
  /**
   * Called when the form is successfully submitted.
   * In create mode, should return the newly created material id so the form
   * can atomically bind any draft markdown images, cover, and video uploaded
   * before save. The parent should NOT trigger navigation from here — use
   * `onAfterSubmit` instead so that draft→bind round-trips finish before the
   * form unmounts.
   */
  onSubmit: (
    values: CreateMaterialRequest | UpdateMaterialRequest,
  ) => Promise<MaterialId | void> | MaterialId | void;
  /**
   * Called after `onSubmit` and any post-submit asset binding has completed.
   * Parents typically navigate here.
   */
  onAfterSubmit?: (materialId: MaterialId | undefined) => void;
  onCancel?: () => void;
  /** Slot for extra actions in the form footer */
  extraActions?: React.ReactNode;
  /** Hide the internal submit button (useful if the page renders one in its own header) */
  hideSubmitButton?: boolean;
  /** Override the submit label */
  submitLabel?: string;
  /** External id for the form element — allows submission from a button outside the form */
  formId?: string;
  /** Show the "send markdown link" warning when content is empty */
  requireContent?: boolean;
  isPending?: boolean;
  /** Render prop for video upload — avoids cross-slice dependency */
  renderVideoUpload?: (props: VideoUploadSlotProps) => React.ReactNode;
  /**
   * Слот блока привязки квиза «Проверь себя» (#494) — features/quiz-builder
   * рендерится страницей (avoids cross-slice dependency, как renderVideoUpload).
   * `onQuizIdChange` немедленно шлёт PATCH (media-autosave path) — привязка
   * работает без полного сабмита формы. Все update-пути формы передают текущий
   * quizId (PUT-семантика: без него бэкенд отвяжет квиз).
   */
  renderQuizSection?: (props: {
    quizId: string | null;
    onQuizIdChange: (quizId: string | null) => void;
  }) => React.ReactNode;
  /** Создаём материал в контексте курса (передаём courseId) — позволяет сразу выбирать ENROLLED. */
  hasCourseContext?: boolean;
  /**
   * Опционально включает auto-save title/content/kind/accessType с debounce.
   * Вызывается только в edit-mode, когда форма dirty. Caller отвечает за
   * вызов backend mutation; ошибки surface через toast в caller'е.
   * Получает полный UpdateMaterialRequest payload (включая current videoId/previewId,
   * чтобы Update-handler не интерпретировал их отсутствие как detach).
   */
  onAutoSave?: (values: UpdateMaterialRequest) => Promise<void>;
}

const DEFAULT_VALUES: MaterialFormValues = {
  title: "",
  content: "",
  description: "",
  kind: "ARTICLE",
  // PUBLIC — единственный access, разрешённый без привязки к курсу (INV-2).
  // Если материал создаётся из контекста курса, страница-обёртка передаст ENROLLED через initialValue.
  accessType: "PUBLIC",
};

function buildDefaults(initial?: MaterialFormInitialValue): MaterialFormValues {
  if (!initial) return DEFAULT_VALUES;
  return {
    title: initial.title,
    content: initial.content ?? "",
    description: initial.description ?? "",
    // Safety-net: если бэкенд по какой-то причине вернёт пустой kind/accessType —
    // не позволяем zod-схеме упасть с "Invalid option", подставляем дефолт.
    kind: initial.kind ?? "ARTICLE",
    accessType: initial.accessType ?? "PUBLIC",
  };
}

export function MaterialForm({
  mode,
  context,
  materialId,
  initialValue,
  material,
  onSubmit,
  onAfterSubmit,
  onCancel,
  extraActions,
  hideSubmitButton = false,
  submitLabel,
  formId = "material-form",
  isPending = false,
  renderVideoUpload,
  renderQuizSection,
  hasCourseContext = false,
  onAutoSave,
}: MaterialFormProps) {
  const queryClient = useQueryClient();
  const form = useForm<MaterialFormValues>({
    resolver: zodResolver(materialFormSchema),
    defaultValues: buildDefaults(initialValue),
    values: initialValue ? buildDefaults(initialValue) : undefined,
  });

  // Stable per-session draft id shared by markdown images, cover image, and
  // video in create mode. Generated once per form mount — see the draft
  // lifecycle docs in FileService/CLAUDE.md. All create-mode media uploads
  // use this id, and on submit they are bound to the real material in one
  // atomic BindDraftAssets call.
  const [mediaDraftId] = useState(() => crypto.randomUUID());

  const markdownAssetParams = materialId
    ? { targetEntity: { type: "material", id: materialId } }
    : { draftId: mediaDraftId };
  const { handleImagePaste } = useMarkdownImageUpload(markdownAssetParams);
  const { handleFileAttach } = useMarkdownFileUpload(markdownAssetParams);

  // Все типы материалов видны в редакторе — иначе материалы с редкими типами
  // открываются с пустым тип-табом и форма отдаёт kind=undefined в zod.
  const visibleKinds: MaterialKind[] = [...MATERIAL_KINDS];

  // Track current video asset id so we can pass it as `videoId` in the
  // create/update payload — backend then performs a sync bind (idempotent
  // if already bound) instead of waiting for the async FileAssetBound event.
  // Initial value comes from the loaded material in edit mode; in create
  // mode it starts as null and is set by the VideoUpload callback once the
  // user uploads or attaches a video.
  const [videoAssetId, setVideoAssetId] = useState<string | null>(material?.videoId ?? null);
  // Ref дублирует videoAssetId — нужен для читать-в-event-handler (callback
  // previewUpload.onChange живёт в options closure'е, тот может lag'ать на
  // render позади). Запись только в event-handler'ах (handleVideoAssetIdChange).
  // Не render-affecting, поэтому react-hooks/refs не трогает.
  const videoAssetIdRef = useRef<string | null>(material?.videoId ?? null);

  // Привязка квиза «Проверь себя» (#489/#494) — та же state+ref пара, что у видео:
  // ref читается в event-time save-коллбеках (autosave/submit), state — в render
  // (слот renderQuizSection). PUT-семантика quizId требует передавать текущее
  // значение в КАЖДОМ update-пути формы, иначе бэкенд отвяжет квиз.
  const [quizId, setQuizId] = useState<string | null>(material?.quizId ?? null);
  const quizIdRef = useRef<string | null>(material?.quizId ?? null);

  // Immediate-save для медиа: загруженное видео/обложка сразу пишутся в материал
  // (`materials.video_id`, `materials.image_id`), а не ждут клика «Сохранить».
  // Триггерится из event-handler'ов upload/delete — каждый handler получает
  // новое значение в аргументе и пробрасывает в `triggerSave`. Сериализация
  // / inflight tracking — внутри хука.
  const { triggerSave: triggerMediaSave, status: mediaSaveStatus } = useMediaAutosave({
    enabled: mode === "edit" && !!materialId && !!onAutoSave,
    initial: {
      videoId: material?.videoId ?? null,
      previewId: material?.imageId ?? null,
      quizId: material?.quizId ?? null,
    },
    save: async (target) => {
      if (!onAutoSave) return;
      const values = form.getValues();
      await onAutoSave({
        title: values.title,
        content: values.content.length > 0 ? values.content : null,
        description: values.description && values.description.length > 0 ? values.description : null,
        kind: values.kind,
        accessType: values.accessType,
        videoId: target.videoId,
        previewId: target.previewId,
        quizId: target.quizId,
      });
    },
  });

  // In create mode we pass a draftId; in edit mode we pass the real
  // materialId. The hook adapts and routes uploads accordingly.
  // onChange — instant-save bridge: при upload/delete обложки получаем новый
  // assetId как аргумент и шлём PATCH с актуальным video state'ом.
  const previewUpload = useUploadMaterialPreview(
    materialId
      ? {
          materialId,
          // onChange runs in event-time (mutation onSuccess/onMutate). Reading
          // ref здесь безопасно и даёт latest videoAssetId независимо от того,
          // когда useUploadMaterialPreview зафиксировал свой callback.
          onChange: (newPreviewId) =>
            triggerMediaSave({
              videoId: videoAssetIdRef.current,
              previewId: newPreviewId,
              quizId: quizIdRef.current,
            }),
        }
      : { draftId: mediaDraftId },
  );

  // Derived: текущий previewId — closure capture для save'ов где надо
  // совмещать текущее видео с актуальной обложкой.
  const currentPreviewId =
    previewUpload.uploadedAssetId ?? (previewUpload.isDeleted ? null : (material?.imageId ?? null));

  // Wraps setVideoAssetId — VideoUpload вызывает onAssetIdChange после успешного
  // upload или delete; помимо обновления state мы немедленно шлём PATCH.
  // Ref synced в этой же event-time функции — preview onChange callback'и потом
  // прочитают latest. Sync setState не пробрасывает в текущий tick, поэтому ref.
  const handleVideoAssetIdChange = (newAssetId: string | null) => {
    setVideoAssetId(newAssetId);
    videoAssetIdRef.current = newAssetId;
    triggerMediaSave({
      videoId: newAssetId,
      previewId: currentPreviewId,
      quizId: quizIdRef.current,
    });
  };

  // Привязка/отвязка квиза из слота renderQuizSection — немедленный PATCH тем же
  // media-autosave путём (атомарный user-action, без debounce и без сабмита формы).
  const handleQuizIdChange = (newQuizId: string | null) => {
    setQuizId(newQuizId);
    quizIdRef.current = newQuizId;
    triggerMediaSave({
      videoId: videoAssetIdRef.current,
      previewId: currentPreviewId,
      quizId: newQuizId,
    });
  };

  // Text auto-save (title/content/kind/accessType) — debounced через form.watch.
  // Мedia auto-save идёт отдельной веткой через triggerMediaSave (выше) — там
  // нет debounce и срабатывает в callback'ах upload-хуков.
  const textAutoSaveStatus: AutosaveStatus = useFormAutosave<MaterialFormValues>({
    form,
    enabled: mode === "edit" && !!onAutoSave,
    save: async (values) => {
      if (!onAutoSave) return;
      // Ref reads safe — useFormAutosave fires save() через debounced setTimeout
      // (event-time, не render). Это даёт latest videoAssetId если юзер сменил
      // его пока debounce таймер крутился.
      await onAutoSave({
        title: values.title,
        content: values.content.length > 0 ? values.content : null,
        description: values.description && values.description.length > 0 ? values.description : null,
        kind: values.kind,
        accessType: values.accessType,
        videoId: videoAssetIdRef.current,
        previewId: currentPreviewId,
        quizId: quizIdRef.current,
      });
    },
  });

  // Объединённый статус — error > saving > saved > idle. Показываем юзеру что
  // и текстовые, и медиа-правки в одном бейдже, чтобы не плодить два индикатора.
  // Sticky-«Сохранено» (не сбрасывается в idle через таймер) — намеренно: юзер
  // печатает и видит "saved" пока следующий ввод не вернёт badge в "saving".
  const autoSaveStatus: AutosaveStatus =
    textAutoSaveStatus === "error" || mediaSaveStatus === "error"
      ? "error"
      : textAutoSaveStatus === "saving" || mediaSaveStatus === "saving"
        ? "saving"
        : textAutoSaveStatus === "saved" || mediaSaveStatus === "saved"
          ? "saved"
          : "idle";

  const video: VideoInfo | null =
    material?.videoId && material.video
      ? {
          id: material.videoId,
          videoId: material.video.externalVideoId,
          status: (material.video.status as VideoInfo["status"]) ?? "waiting_for_upload",
          thumbnailUrl: material.video.thumbnailUrl,
          duration: material.video.durationSeconds,
          isOwnedStorage: !!material.video.externalVideoId,
        }
      : null;

  const handleVideoChange = (hasVideo?: boolean) => {
    if (materialId) {
      invalidateMaterials(queryClient);
    }
    // Auto-suggest Kind based on video presence
    const currentKind = form.getValues("kind");
    if (hasVideo && currentKind === "ARTICLE") {
      form.setValue("kind", "VIDEO");
    } else if (!hasVideo && currentKind === "VIDEO") {
      form.setValue("kind", "ARTICLE");
    }
  };

  const handleValid = async (values: MaterialFormValues) => {
    // Resolve the current preview asset id: a freshly uploaded one wins,
    // otherwise fall back to the existing material image (edit mode).
    const previewId =
      previewUpload.uploadedAssetId ??
      (previewUpload.isDeleted ? null : (material?.imageId ?? null));

    const base = {
      title: values.title,
      content: values.content.length > 0 ? values.content : null,
      description: values.description && values.description.length > 0 ? values.description : null,
      kind: values.kind,
      accessType: values.accessType,
    };

    // Sync media binding (2026-04-25): videoId/previewId pass through the
    // payload so backend BindAssetAsync attaches them in the same transaction
    // as the material save. Markdown images are still bound asynchronously
    // via BindMaterialDraftAssets event when draftId is set.
    const payload: CreateMaterialRequest | UpdateMaterialRequest =
      mode === "create"
        ? {
            ...base,
            draftId: mediaDraftId,
            videoId: videoAssetId ?? undefined,
            previewId: previewId ?? undefined,
            quizId: quizId ?? undefined,
          }
        : {
            ...base,
            videoId: videoAssetId,
            previewId,
            // PUT-семантика (#489): отсутствие quizId бэкенд трактует как detach.
            quizId,
          };

    const submitResult = await onSubmit(payload);

    if (mode === "edit" && materialId && values.content) {
      await bindMarkdownAssets({
        mode: "edit",
        targetEntity: { type: "material", id: materialId },
        markdownText: values.content,
      }).catch(() => {
        toast.warning("Не удалось привязать изображения к материалу");
      });
    }

    onAfterSubmit?.(
      mode === "edit" ? (materialId as MaterialId | undefined) : submitResult || undefined,
    );
  };

  const handleInvalid = (errors: FieldErrors<MaterialFormValues>) => {
    const firstError =
      errors.title?.message ??
      errors.content?.message ??
      errors.description?.message ??
      errors.kind?.message ??
      errors.accessType?.message ??
      "Проверьте заполнение формы";
    toast.error(firstError);

    if (errors.title) {
      form.setFocus("title");
    }
  };

  const layoutClass =
    context === "full-page"
      ? "mx-auto w-full max-w-7xl space-y-6 px-4 py-6 md:px-6 md:py-8"
      : "flex flex-col gap-5 px-5 py-4";

  return (
    <form
      id={formId}
      onSubmit={form.handleSubmit((values) => void handleValid(values), handleInvalid)}
      className={layoutClass}
    >
      <div className="space-y-2">
        <div className="flex items-center justify-between">
          <Label htmlFor={`${formId}-title`}>Название</Label>
          {mode === "edit" && onAutoSave && <AutosaveBadge status={autoSaveStatus} />}
        </div>
        <Input
          id={`${formId}-title`}
          placeholder="Введите название материала"
          {...form.register("title")}
        />
        {form.formState.errors.title && (
          <p className="text-sm text-destructive">{form.formState.errors.title.message}</p>
        )}
      </div>

      <Controller
        control={form.control}
        name="kind"
        render={({ field }) => (
          <div className="flex flex-wrap items-center gap-2">
            {visibleKinds.map((kind) => {
              const badge = getMaterialKindBadge(kind);
              const Icon = kindIcons[kind];
              const active = field.value === kind;
              return (
                <button
                  key={kind}
                  type="button"
                  onClick={() => field.onChange(kind)}
                  className={cn(
                    "inline-flex min-h-[44px] items-center gap-1.5 rounded-lg border px-3 py-1.5 text-sm font-medium transition-colors sm:min-h-0",
                    active
                      ? badge.className
                      : "border-border/70 text-muted-foreground hover:text-foreground",
                  )}
                  aria-pressed={active}
                >
                  <Icon size={14} />
                  {badge.label}
                </button>
              );
            })}
          </div>
        )}
      />

      <div className="space-y-2">
        <Label>Уровень доступа</Label>
        <Controller
          control={form.control}
          name="accessType"
          render={({ field }) => {
            // ENROLLED работает и для orphan-материалов: гейт через platform full-access (`plan:all`).
            // hasCourseBinding используется только для смены подсказки селектора:
            // course-bound → «полная запись», orphan → «полный доступ платформы».
            const courseCount = mode === "edit" ? (material?.courseCount ?? 0) : 0;
            const hasCourseBinding = courseCount > 0 || hasCourseContext;
            return (
              <AccessTypeSelector
                value={field.value}
                onChange={(v) => field.onChange(v as MaterialAccessType)}
                hasCourseBinding={hasCourseBinding}
              />
            );
          }}
        />
      </div>

      <div className="grid gap-4 max-w-2xl md:grid-cols-2">
        <div className="space-y-2">
          <div className="flex items-center gap-2">
            <Icons.film size={15} className="text-muted-foreground" />
            <Label>Видео</Label>
          </div>
          {renderVideoUpload?.({
            entityId: materialId ?? null,
            entityType: EntityTypes.MATERIAL,
            draftId: materialId ? null : mediaDraftId,
            video,
            // handleVideoAssetIdChange читает videoAssetIdRef внутри —
            // вызовётся в event-time (mutation onSuccess), не в render.
            onVideoChange: handleVideoChange,
            onAssetIdChange: handleVideoAssetIdChange,
            entityLabel: "материала",
          })}
        </div>

        <div className="space-y-2">
          <div className="flex items-center gap-2">
            <Icons.uploadImage size={15} className="text-muted-foreground" />
            <Label>Обложка</Label>
          </div>
          <MaterialPreviewUpload
            hook={previewUpload}
            imageId={material?.imageId}
            initialPreviewUrl={material?.imageUrl}
          />
        </div>
      </div>

      {mode === "edit" && material && (
        <div className="space-y-2">
          <Label>Теги</Label>
          <TagsField key={material.id} entityId={material.id} entityType={EntityTypes.MATERIAL} />
        </div>
      )}

      {videoAssetId && (
        <VideoAiProcessingPanel
          videoId={videoAssetId}
          materialId={materialId ?? null}
          hasMaterialContent={!!form.watch("content")?.trim()}
        />
      )}

      {/* Авторское «Описание» отдельно от content: content используется как конспект. */}
      <div className="flex flex-col gap-2">
        <div className="flex items-center justify-between gap-3">
          <div className="space-y-0.5">
            <Label htmlFor={`${formId}-description`}>Описание</Label>
            <p className="text-xs text-muted-foreground">
              Кратко объясните, о чём материал и что ученик найдёт внутри
            </p>
          </div>
          {form.formState.errors.description && (
            <p className="text-sm text-destructive">{form.formState.errors.description.message}</p>
          )}
        </div>
        <Controller
          control={form.control}
          name="description"
          render={({ field }) => (
            <MarkdownEditor
              value={field.value ?? ""}
              onChange={field.onChange}
              onImagePaste={handleImagePaste}
              onFileAttach={handleFileAttach}
              minHeight={context === "full-page" ? 220 : 160}
              placeholder="Например: разберём базовую настройку проекта, ключевые команды и частые ошибки…"
              className="bg-card/70"
              layout="split"
            />
          )}
        />
      </div>

      <div className="flex flex-col gap-2">
        <div className="flex items-center justify-between gap-3">
          <div className="space-y-0.5">
            <Label htmlFor={`${formId}-content`}>Конспект</Label>
            <p className="text-xs text-muted-foreground">
              Необязательно: основной Markdown-конспект, ссылки и материалы урока
            </p>
          </div>
          {form.formState.errors.content && (
            <p className="text-sm text-destructive">{form.formState.errors.content.message}</p>
          )}
        </div>
        <Controller
          control={form.control}
          name="content"
          render={({ field }) => (
            <MarkdownEditor
              value={field.value}
              onChange={field.onChange}
              onImagePaste={handleImagePaste}
              onFileAttach={handleFileAttach}
              minHeight={context === "full-page" ? 520 : 320}
              placeholder="Напишите конспект материала в формате Markdown..."
              className="bg-card/70"
              layout="split"
            />
          )}
        />
      </div>

      {/* Блок привязки квиза «Проверь себя» (#494) — рендерится страницей через слот. */}
      {renderQuizSection?.({ quizId, onQuizIdChange: handleQuizIdChange })}

      {!hideSubmitButton && (
        <div className="flex flex-wrap items-center justify-end gap-2 border-t border-border/50 pt-4">
          {extraActions}
          {onCancel && (
            <Button
              type="button"
              variant="ghost"
              className="min-touch flex-1 sm:flex-none"
              onClick={onCancel}
            >
              Отмена
            </Button>
          )}
          <Button type="submit" disabled={isPending} className="min-touch flex-1 sm:flex-none">
            <Icons.save size={14} />
            {submitLabel ??
              (isPending ? "Сохранение..." : mode === "edit" ? "Сохранить" : "Создать")}
          </Button>
        </div>
      )}
    </form>
  );
}

function AutosaveBadge({ status }: { status: AutosaveStatus }) {
  if (status === "idle") return null;

  const config = {
    saving: {
      icon: <Icons.loading size={12} className="animate-spin" />,
      text: "Сохраняется…",
      className: "text-muted-foreground",
    },
    saved: {
      icon: <Icons.check size={12} />,
      text: "Сохранено",
      className: "text-emerald-600 dark:text-emerald-400",
    },
    error: {
      icon: <Icons.warning size={12} />,
      text: "Ошибка автосохранения",
      className: "text-destructive",
    },
  } as const;

  const { icon, text, className } = config[status];

  return (
    <span
      className={cn("inline-flex items-center gap-1.5 text-xs", className)}
      role="status"
      aria-live="polite"
    >
      {icon}
      {text}
    </span>
  );
}

export function toMaterialFormInitialValue(material: MaterialDetailDto): MaterialFormInitialValue {
  return {
    title: material.title,
    content: material.content ?? "",
    description: material.description ?? "",
    kind: material.kind,
    accessType: material.accessType,
  };
}
