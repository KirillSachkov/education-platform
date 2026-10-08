"use client";

import {
  TRAINER_TOPIC_DIRECTIONS,
  type CreateTrainerTopicBody,
  type TrainerTopicAdmin,
  type UpdateTrainerTopicBody,
} from "@/entities/trainer-admin-content";
import { trainerTracksQueryOptions, type TrainerTrack } from "@/entities/trainer-track";
import { TRAINER_DIRECTION_LABELS } from "@/shared/config/trainer";
import { FormDialog } from "@/shared/ui/components/form-dialog";
import { Input } from "@/shared/ui/kit/input";
import { Label } from "@/shared/ui/kit/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/shared/ui/kit/select";
import { Textarea } from "@/shared/ui/kit/textarea";
import { useQuery } from "@tanstack/react-query";
import { Controller, useForm } from "react-hook-form";
import { useCreateTrainerTopic, useUpdateTrainerTopic } from "../model/use-topic-mutations";
import { suggestTopicSlug } from "../model/slug";

/** Сентинел «без направления» в Select (RHF хранит "" → отправляем null). */
const NO_DIRECTION = "__none__";

interface TopicFormValues {
  trackId: string;
  slug: string;
  title: string;
  area: string;
  description: string;
  direction: string;
  recommendedCourseId: string;
  fallbackCourseId: string;
}

interface TopicFormDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** Тема для редактирования; `null` → режим создания (slug редактируем). */
  topic: TrainerTopicAdmin | null;
  /** Трек для предзаполнения в create-режиме (текущий фильтр списка). */
  defaultTrackId?: string;
  /** Получает id созданной темы (для авто-выбора). */
  onCreated?: (topicId: string) => void;
}

/**
 * Диалог создания/редактирования темы тренажёра (#623). В create-режиме slug
 * редактируется (immutable на бэке после создания) и авто-подсказывается из
 * названия; в edit-режиме поле slug скрыто. Направление/курсы — опциональны.
 */
export function TopicFormDialog({
  open,
  onOpenChange,
  topic,
  defaultTrackId,
  onCreated,
}: TopicFormDialogProps) {
  const isEdit = topic !== null;
  const { data: tracks } = useQuery(trainerTracksQueryOptions.tracksOptions());
  const createMutation = useCreateTrainerTopic();
  const updateMutation = useUpdateTrainerTopic();
  const isPending = createMutation.isPending || updateMutation.isPending;

  const {
    register,
    handleSubmit,
    control,
    reset,
    setValue,
    getValues,
    formState: { errors },
  } = useForm<TopicFormValues>({
    values: {
      trackId: topic?.trackId ?? defaultTrackId ?? "",
      slug: topic?.slug ?? "",
      title: topic?.title ?? "",
      area: topic?.area ?? "",
      description: topic?.description ?? "",
      direction: topic?.direction ?? "",
      recommendedCourseId: topic?.recommendedCourseId ?? "",
      fallbackCourseId: topic?.fallbackCourseId ?? "",
    },
  });

  // Авто-slug из названия только в create-режиме, пока пользователь сам slug не трогал.
  const handleTitleBlur = () => {
    if (isEdit) return;
    const slug = getValues("slug").trim();
    const title = getValues("title").trim();
    if (slug.length === 0 && title.length > 0) {
      setValue("slug", suggestTopicSlug(title), { shouldValidate: true });
    }
  };

  const onSubmit = handleSubmit((values) => {
    const direction = values.direction.trim() === "" ? null : values.direction.trim();
    const description = values.description.trim() === "" ? null : values.description.trim();
    const recommendedCourseId =
      values.recommendedCourseId.trim() === "" ? null : values.recommendedCourseId.trim();
    const fallbackCourseId =
      values.fallbackCourseId.trim() === "" ? null : values.fallbackCourseId.trim();

    if (isEdit) {
      const body: UpdateTrainerTopicBody = {
        trackId: values.trackId,
        title: values.title.trim(),
        area: values.area.trim(),
        description,
        direction,
        recommendedCourseId,
        fallbackCourseId,
      };
      updateMutation.mutate(
        { topicId: topic.id, body },
        {
          onSuccess: () => {
            onOpenChange(false);
          },
        },
      );
      return;
    }

    const body: CreateTrainerTopicBody = {
      trackId: values.trackId,
      slug: values.slug.trim(),
      title: values.title.trim(),
      area: values.area.trim(),
      description,
      direction,
      recommendedCourseId,
      fallbackCourseId,
    };
    createMutation.mutate(body, {
      onSuccess: (topicId) => {
        onOpenChange(false);
        reset();
        onCreated?.(topicId);
      },
    });
  });

  return (
    <FormDialog
      open={open}
      onOpenChange={(next) => {
        onOpenChange(next);
        if (!next && !isEdit) reset();
      }}
      title={isEdit ? "Изменить тему" : "Создать тему"}
      description={
        isEdit
          ? "Slug темы изменить нельзя — он фиксируется при создании."
          : "Тема создаётся черновиком — банки вопросов привяжете после."
      }
      onSubmit={onSubmit}
      isPending={isPending}
      submitLabel={isEdit ? "Сохранить" : "Создать"}
      contentClassName="sm:max-w-[560px]"
    >
      <div className="space-y-1.5">
        <Label htmlFor="topic-track">Трек</Label>
        <Controller
          control={control}
          name="trackId"
          rules={{ required: true }}
          render={({ field }) => (
            <Select value={field.value} onValueChange={field.onChange}>
              <SelectTrigger id="topic-track" className="w-full">
                <SelectValue placeholder="Выберите трек" />
              </SelectTrigger>
              <SelectContent>
                {(tracks ?? []).map((track: TrainerTrack) => (
                  <SelectItem key={track.id} value={track.id}>
                    {track.title}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          )}
        />
        {errors.trackId && <p className="text-xs text-destructive">Выберите трек</p>}
      </div>

      {!isEdit && (
        <div className="space-y-1.5">
          <Label htmlFor="topic-slug">Slug</Label>
          <Input
            id="topic-slug"
            {...register("slug", { required: true })}
            placeholder="например: async-await"
          />
          {errors.slug && <p className="text-xs text-destructive">Укажите slug</p>}
          <p className="text-xs text-muted-foreground">
            Строчные латинские буквы, цифры и дефис. Изменить позже нельзя.
          </p>
        </div>
      )}

      <div className="space-y-1.5">
        <Label htmlFor="topic-title">Название</Label>
        <Input
          id="topic-title"
          {...register("title", { required: true })}
          onBlur={handleTitleBlur}
          maxLength={200}
          placeholder="Например: Асинхронность в C#"
        />
        {errors.title && <p className="text-xs text-destructive">Укажите название</p>}
      </div>

      <div className="space-y-1.5">
        <Label htmlFor="topic-area">Область</Label>
        <Input
          id="topic-area"
          {...register("area", { required: true })}
          maxLength={200}
          placeholder="Например: Язык и платформа"
        />
        {errors.area && <p className="text-xs text-destructive">Укажите область</p>}
      </div>

      <div className="space-y-1.5">
        <Label htmlFor="topic-direction">Направление</Label>
        <Controller
          control={control}
          name="direction"
          render={({ field }) => (
            <Select
              value={field.value === "" ? NO_DIRECTION : field.value}
              onValueChange={(v) => field.onChange(v === NO_DIRECTION ? "" : v)}
            >
              <SelectTrigger id="topic-direction" className="w-full">
                <SelectValue placeholder="Без направления" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value={NO_DIRECTION}>Без направления</SelectItem>
                {TRAINER_TOPIC_DIRECTIONS.map((dir) => (
                  <SelectItem key={dir} value={dir}>
                    {TRAINER_DIRECTION_LABELS[dir] ?? dir}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          )}
        />
      </div>

      <div className="space-y-1.5">
        <Label htmlFor="topic-description">Описание</Label>
        <Textarea
          id="topic-description"
          {...register("description")}
          rows={3}
          placeholder="Короткое описание темы (необязательно)"
        />
      </div>

      <div className="grid gap-4 sm:grid-cols-2">
        <div className="space-y-1.5">
          <Label htmlFor="topic-recommended-course">Рекомендуемый курс (ID)</Label>
          <Input
            id="topic-recommended-course"
            {...register("recommendedCourseId")}
            placeholder="UUID курса (необязательно)"
          />
        </div>
        <div className="space-y-1.5">
          <Label htmlFor="topic-fallback-course">Запасной курс (ID)</Label>
          <Input
            id="topic-fallback-course"
            {...register("fallbackCourseId")}
            placeholder="UUID курса (необязательно)"
          />
        </div>
      </div>
    </FormDialog>
  );
}
