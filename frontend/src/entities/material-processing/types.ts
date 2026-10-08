export type VideoProcessingStatus =
  | "QUEUED"
  | "PROCESSING"
  | "COMPLETED"
  | "FAILED";

export type VideoProcessingStage =
  | "QUEUED"
  | "SOURCE_FETCH"
  | "PROBE"
  | "AUDIO_EXTRACT"
  | "TRANSCRIBE"
  | "GENERATE"
  | "SAVE";

export type TranscriptPreparationSource = "TIMECODES" | "CONTENT";

export interface TranscriptPreparationDto {
  jobId: string;
  source: TranscriptPreparationSource;
  status: VideoProcessingStatus;
  stage: VideoProcessingStage;
  progressPercent: number;
  materialId: string | null;
  errorCode: string | null;
  errorMessage: string | null;
  requestedAt: string;
}

export type TimecodeGenerationMode = "TIMECODES" | "TRANSCRIPT_ONLY";

export interface TimecodeGenerationDto {
  jobId: string;
  status: VideoProcessingStatus;
  stage: VideoProcessingStage;
  progressPercent: number;
  errorCode: string | null;
  errorMessage: string | null;
  requestedAt: string;
  /**
   * "TIMECODES" — полный pipeline (транскрипт → AI тайм-коды → Kinescope chapters).
   * "TRANSCRIPT_ONLY" — только транскрипт; после COMPLETED главы НЕ созданы,
   *   автор может отдельно нажать «Сгенерировать тайм-коды».
   */
  mode: TimecodeGenerationMode;
}

export type ActiveAiJobKind = "TRANSCRIPT" | "TIMECODES" | "CONTENT";

export interface ActiveAiJobDto {
  jobId: string;
  jobKind: ActiveAiJobKind;
  videoAssetId: string;
  /** Только для CONTENT-job'ов — для deeplink на материал. */
  materialId: string | null;
  status: VideoProcessingStatus;
  stage: VideoProcessingStage;
  progressPercent: number;
  errorCode: string | null;
  errorMessage: string | null;
  createdAt: string;
}

export interface GetActiveAiJobsResponse {
  jobs: ActiveAiJobDto[];
}

export interface ActiveContentGenerationDto {
  jobId: string;
  materialId: string;
  status: VideoProcessingStatus;
  stage: VideoProcessingStage;
  progressPercent: number;
  errorCode: string | null;
  errorMessage: string | null;
  requestedAt: string;
}

export interface GetVideoTimecodesResponse {
  videoId: string;
  assetVersion: string | null;
  hasTranscript: boolean;
  transcriptPreparation: TranscriptPreparationDto | null;
  generation: TimecodeGenerationDto | null;
  activeContentGeneration: ActiveContentGenerationDto | null;
}

export interface GenerateVideoTimecodesResponse {
  jobId: string;
  videoId: string;
  status: VideoProcessingStatus;
}

export interface GenerateVideoContentRequest {
  materialId: string;
  /**
   * При false (default) backend возвращает 409 `material.content.exists` если
   * у материала уже есть руками написанное тело. Передайте true чтобы AI
   * молча перезаписал — UI должен показать confirmation перед этим.
   */
  forceOverwrite?: boolean;
  /**
   * Optional admin model override (например "openai/gpt-4.1-nano"). Игнорируется
   * backend'ом если caller не admin.
   */
  modelOverride?: string;
}

export interface GenerateVideoContentResponse {
  jobId: string;
  videoId: string;
  materialId: string;
  status: VideoProcessingStatus;
}

export interface AiUsageRow {
  day: string; // "YYYY-MM-DD"
  jobKind: "TIMECODES" | "CONTENT";
  model: string; // "<default>" если override не задан
  status: VideoProcessingStatus;
  count: number;
}

export interface GetAiUsageResponse {
  sinceUtc: string;
  days: number;
  transcriptsCreated: number;
  rows: AiUsageRow[];
}

export interface AiModelSlotDto {
  model: string;
  temperature: number | null;
  maxOutputTokens: number | null;
  timeoutSeconds: number | null;
}

export type AiModelSettingsSource = "CONFIG" | "DATABASE";

export interface AiModelSettingsDto {
  speechToText: AiModelSlotDto;
  timecodeGeneration: AiModelSlotDto;
  contentGeneration: AiModelSlotDto;
  /**
   * Авто-запуск транскрипции + тайм-кодов, когда загруженное видео готово (#648).
   * Default на бэке — true.
   */
  autoProcessVideosEnabled: boolean;
  source: AiModelSettingsSource;
  updatedAtUtc: string | null;
  updatedByUserId: string | null;
}

export interface UpdateAiModelSettingsRequest {
  speechToText: AiModelSlotDto;
  timecodeGeneration: AiModelSlotDto;
  contentGeneration: AiModelSlotDto;
  autoProcessVideosEnabled: boolean;
}
