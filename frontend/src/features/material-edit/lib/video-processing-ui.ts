import type {
  ActiveContentGenerationDto,
  GetVideoTimecodesResponse,
  TimecodeGenerationDto,
  TranscriptPreparationDto,
  VideoProcessingStage,
  VideoProcessingStatus,
} from "@/entities/material-processing";

export const PROCESSING_ACTIVE_STATUSES: VideoProcessingStatus[] = [
  "QUEUED",
  "PROCESSING",
];

export function isProcessingActive(
  status: VideoProcessingStatus | null | undefined,
): boolean {
  return !!status && PROCESSING_ACTIVE_STATUSES.includes(status);
}

export function getStageLabel(stage: VideoProcessingStage | null | undefined) {
  switch (stage) {
    case "QUEUED":
      return "В очереди";
    case "SOURCE_FETCH":
      return "Получаем видео";
    case "PROBE":
      return "Проверяем видео";
    case "AUDIO_EXTRACT":
      return "Извлекаем аудио";
    case "TRANSCRIBE":
      return "Расшифровываем аудио";
    case "GENERATE":
      return "Генерируем результат";
    case "SAVE":
      return "Сохраняем";
    default:
      return "Ожидаем";
  }
}

export function getStatusLabel(status: VideoProcessingStatus | null | undefined) {
  switch (status) {
    case "QUEUED":
      return "В очереди";
    case "PROCESSING":
      return "В работе";
    case "COMPLETED":
      return "Готово";
    case "FAILED":
      return "Ошибка";
    default:
      return "Нет статуса";
  }
}

export type PrimaryProcessingJob =
  | TranscriptPreparationDto
  | TimecodeGenerationDto
  | ActiveContentGenerationDto;

export function getPrimaryProcessingJob(
  status: GetVideoTimecodesResponse | undefined,
): PrimaryProcessingJob | null {
  if (!status) return null;
  if (status.transcriptPreparation) return status.transcriptPreparation;
  if (status.activeContentGeneration) return status.activeContentGeneration;
  if (status.generation) return status.generation;
  return null;
}

export function hasActiveVideoProcessing(
  status: GetVideoTimecodesResponse | undefined,
): boolean {
  if (!status) return false;
  return (
    isProcessingActive(status.transcriptPreparation?.status) ||
    isProcessingActive(status.activeContentGeneration?.status) ||
    isProcessingActive(status.generation?.status)
  );
}

export function formatVideoTime(totalSeconds: number): string {
  const safeSeconds = Math.max(0, Math.floor(totalSeconds));
  const hours = Math.floor(safeSeconds / 3600);
  const minutes = Math.floor((safeSeconds % 3600) / 60);
  const seconds = safeSeconds % 60;

  if (hours > 0) {
    return `${hours}:${minutes.toString().padStart(2, "0")}:${seconds
      .toString()
      .padStart(2, "0")}`;
  }

  return `${minutes}:${seconds.toString().padStart(2, "0")}`;
}

export function parseVideoTime(value: string): number | null {
  const trimmed = value.trim();
  if (!trimmed) return null;

  if (/^\d+$/.test(trimmed)) {
    return Number(trimmed);
  }

  const parts = trimmed.split(":");
  if (parts.length < 2 || parts.length > 3) return null;
  if (parts.some((part) => !/^\d+$/.test(part))) return null;

  const numbers = parts.map(Number);
  if (numbers.some((part) => Number.isNaN(part))) return null;

  if (numbers.length === 2) {
    const [minutes, seconds] = numbers;
    if (seconds > 59) return null;
    return minutes * 60 + seconds;
  }

  const [hours, minutes, seconds] = numbers;
  if (minutes > 59 || seconds > 59) return null;
  return hours * 3600 + minutes * 60 + seconds;
}
