"use client";

import type { TrainerTopicAdmin } from "@/entities/trainer-admin-content";
import { trainerTracksQueryOptions } from "@/entities/trainer-track";
import { ROLES, RequireRole } from "@/shared/auth";
import { Button } from "@/shared/ui/kit/button";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/shared/ui/kit/select";
import { Icons } from "@/shared/ui/icons";
import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { TopicFormDialog } from "./topic-form-dialog";
import { TopicsTable } from "./topics-table";

/** Сентинел «все треки» в Select фильтра. */
const ALL_TRACKS = "__all__";

/**
 * Страница админ-управления контентом тренажёра (#623): фильтр по треку, список
 * тем (таблица/карточки) с раскрытием панели банков, создание/редактирование
 * темы через `TopicFormDialog`. Гейт на ADMIN — в `admin/layout.tsx`.
 */
export function TrainerContentManager() {
  const { data: tracks } = useQuery(trainerTracksQueryOptions.tracksOptions());
  const [trackFilter, setTrackFilter] = useState<string>(ALL_TRACKS);
  const [dialogOpen, setDialogOpen] = useState(false);
  const [editingTopic, setEditingTopic] = useState<TrainerTopicAdmin | null>(null);

  const activeTrackId = trackFilter === ALL_TRACKS ? undefined : trackFilter;

  const openCreate = () => {
    setEditingTopic(null);
    setDialogOpen(true);
  };

  const openEdit = (topic: TrainerTopicAdmin) => {
    setEditingTopic(topic);
    setDialogOpen(true);
  };

  return (
    <RequireRole atLeast={ROLES.ADMIN}>
      <div className="mx-auto mt-8 max-w-5xl space-y-6 px-4 pb-16">
        <header className="flex flex-wrap items-end justify-between gap-3">
        <div className="space-y-1">
          <h1 className="text-2xl font-semibold tracking-tight">Тренажёр</h1>
          <p className="text-sm text-muted-foreground">
            Темы тренажёра и их собственные банки вопросов. Тема видна студентам только после
            публикации.
          </p>
        </div>
        <Button onClick={openCreate}>
          <Icons.add className="size-4" />
          Создать тему
        </Button>
      </header>

      <div className="flex items-center gap-2">
        <span className="text-sm text-muted-foreground">Трек:</span>
        <Select value={trackFilter} onValueChange={setTrackFilter}>
          <SelectTrigger className="w-full sm:w-[260px]" aria-label="Фильтр по треку">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={ALL_TRACKS}>Все треки</SelectItem>
            {(tracks ?? []).map((track) => (
              <SelectItem key={track.id} value={track.id}>
                {track.title}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>

      <TopicsTable trackId={activeTrackId} onCreate={openCreate} onEdit={openEdit} />

        <TopicFormDialog
          open={dialogOpen}
          onOpenChange={setDialogOpen}
          topic={editingTopic}
          defaultTrackId={activeTrackId}
        />
      </div>
    </RequireRole>
  );
}
