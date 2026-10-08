"use client";

import { useRef, useState } from "react";
import type { AutosaveStatus } from "./use-form-autosave";

interface MediaTarget {
  videoId: string | null;
  previewId: string | null;
  /** Привязка квиза «Проверь себя» (#489) — PUT-семантика, как у видео/обложки. */
  quizId: string | null;
}

interface UseMediaAutosaveOptions {
  enabled: boolean;
  initial: MediaTarget;
  /** Caller вызывает для сохранения — обычно делегирует mutation. Throws on error. */
  save: (target: MediaTarget) => Promise<void>;
}

function targetsEqual(a: MediaTarget, b: MediaTarget): boolean {
  return a.videoId === b.videoId && a.previewId === b.previewId && a.quizId === b.quizId;
}

/**
 * Immediate-save для медиа-полей (видео, обложка) и привязки квиза. В отличие от
 * `useFormAutosave` — нет debounce'а: media changes — атомарные user-actions
 * (upload-complete / клик «удалить» / выбор квиза в picker'е), не printable input.
 *
 * Сериализация: если пользователь успевает сменить видео А → Б до возврата
 * первого PATCH'а, второй ждёт первый и идёт следом. Last-write-wins. Без
 * этого порядок ответов backend'а мог развалить server-state из-за network
 * reorder.
 */
export function useMediaAutosave({ enabled, initial, save }: UseMediaAutosaveOptions): {
  triggerSave: (target: MediaTarget) => void;
  status: AutosaveStatus;
} {
  const lastSavedRef = useRef<MediaTarget>(initial);
  const inflightRef = useRef(false);
  const pendingTargetRef = useRef<MediaTarget | null>(null);
  const [status, setStatus] = useState<AutosaveStatus>("idle");

  const runLoop = async (initialTarget: MediaTarget): Promise<void> => {
    inflightRef.current = true;
    setStatus("saving");
    let target = initialTarget;
    while (true) {
      try {
        await save(target);
        lastSavedRef.current = target;
        setStatus("saved");
      } catch {
        setStatus("error");
      }
      const pending = pendingTargetRef.current;
      pendingTargetRef.current = null;
      if (!pending || targetsEqual(pending, lastSavedRef.current)) {
        break;
      }
      target = pending;
      setStatus("saving");
    }
    inflightRef.current = false;
  };

  const triggerSave = (target: MediaTarget) => {
    if (!enabled) return;
    if (targetsEqual(target, lastSavedRef.current)) {
      return;
    }
    if (inflightRef.current) {
      pendingTargetRef.current = target;
      return;
    }
    void runLoop(target);
  };

  return { triggerSave, status };
}
