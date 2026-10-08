"use client";

import {
  trainerAdminContentQueryOptions,
  type TrainerTopicAdmin,
} from "@/entities/trainer-admin-content";
import { getErrorMessage } from "@/shared/api";
import { TRAINER_DIRECTION_LABELS } from "@/shared/config/trainer";
import { cn } from "@/shared/lib/css";
import { DeleteConfirmDialog } from "@/shared/ui/components/delete-confirm-dialog";
import { StatusBadge } from "@/shared/ui/components/status-badge";
import { Button } from "@/shared/ui/kit/button";
import { Card, CardContent } from "@/shared/ui/kit/card";
import { EmptyState } from "@/shared/ui/kit/empty-state";
import { Skeleton } from "@/shared/ui/kit/skeleton";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/shared/ui/kit/table";
import { Icons } from "@/shared/ui/icons";
import { useQuery } from "@tanstack/react-query";
import { Eye, EyeOff, Pencil, Trash2 } from "lucide-react";
import { useState } from "react";
import {
  useDeleteTrainerTopic,
  useSetTrainerTopicPublished,
} from "../model/use-topic-mutations";
import { BanksPanel } from "./banks-panel";

interface TopicsTableProps {
  trackId?: string;
  onCreate: () => void;
  onEdit: (topic: TrainerTopicAdmin) => void;
}

/**
 * Список тем тренажёра (admin): десктоп — `<Table>`, мобайл — стопка карточек из
 * того же массива (#370 convention). Строка раскрывает панель банков; в шапке —
 * статус публикации, число банков, экшены (publish-toggle / edit / delete).
 */
export function TopicsTable({ trackId, onCreate, onEdit }: TopicsTableProps) {
  const { data: topics, isLoading, error } = useQuery(
    trainerAdminContentQueryOptions.topicsOptions({ trackId }),
  );
  const [expandedId, setExpandedId] = useState<string | null>(null);

  if (isLoading) {
    return (
      <div className="space-y-3">
        <Skeleton className="h-16 w-full" />
        <Skeleton className="h-16 w-full" />
        <Skeleton className="h-16 w-full" />
      </div>
    );
  }

  if (error) {
    return (
      <p className="text-sm text-destructive">
        {getErrorMessage(error, "Не удалось загрузить темы")}
      </p>
    );
  }

  if (!topics || topics.length === 0) {
    return (
      <EmptyState
        variant="dashed"
        icon={Icons.layers}
        title="Тем пока нет"
        description="Создайте тему тренажёра, чтобы привязать к ней банки вопросов."
        action={
          <Button onClick={onCreate}>
            <Icons.add className="size-4" />
            Создать тему
          </Button>
        }
      />
    );
  }

  const toggleExpanded = (id: string) =>
    setExpandedId((current) => (current === id ? null : id));

  return (
    <div className="space-y-4">
      {/* Desktop table */}
      <div className="hidden overflow-hidden rounded-xl border md:block">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Тема</TableHead>
              <TableHead>Область</TableHead>
              <TableHead>Направление</TableHead>
              <TableHead>Статус</TableHead>
              <TableHead className="text-center">Банки</TableHead>
              <TableHead className="text-right">Действия</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {topics.map((topic) => (
              <TopicTableRow
                key={topic.id}
                topic={topic}
                isExpanded={expandedId === topic.id}
                onToggle={() => toggleExpanded(topic.id)}
                onEdit={() => onEdit(topic)}
              />
            ))}
          </TableBody>
        </Table>
      </div>

      {/* Mobile cards */}
      <div className="space-y-3 md:hidden">
        {topics.map((topic) => (
          <TopicCard
            key={topic.id}
            topic={topic}
            isExpanded={expandedId === topic.id}
            onToggle={() => toggleExpanded(topic.id)}
            onEdit={() => onEdit(topic)}
          />
        ))}
      </div>
    </div>
  );
}

/** Экшены строки темы: publish-toggle, edit, delete (+ 409 has.banks toast). */
function useTopicRowActions(topic: TrainerTopicAdmin) {
  const publishMutation = useSetTrainerTopicPublished();
  const deleteMutation = useDeleteTrainerTopic();
  const [deleteOpen, setDeleteOpen] = useState(false);

  const togglePublish = () =>
    publishMutation.mutate({ topicId: topic.id, publish: !topic.isPublished });

  return {
    publishMutation,
    deleteMutation,
    deleteOpen,
    setDeleteOpen,
    togglePublish,
  };
}

function TopicActions({
  topic,
  onEdit,
}: {
  topic: TrainerTopicAdmin;
  onEdit: () => void;
}) {
  const {
    publishMutation,
    deleteMutation,
    deleteOpen,
    setDeleteOpen,
    togglePublish,
  } = useTopicRowActions(topic);

  return (
    <div className="flex items-center justify-end gap-1.5">
      <Button
        variant="outline"
        size="icon"
        className="min-touch"
        aria-label={topic.isPublished ? "Снять с публикации" : "Опубликовать"}
        title={topic.isPublished ? "Снять с публикации" : "Опубликовать"}
        disabled={publishMutation.isPending}
        onClick={togglePublish}
      >
        {topic.isPublished ? <EyeOff size={16} /> : <Eye size={16} />}
      </Button>
      <Button
        variant="outline"
        size="icon"
        className="min-touch"
        aria-label="Изменить"
        title="Изменить"
        onClick={onEdit}
      >
        <Pencil size={16} />
      </Button>
      <Button
        variant="outline"
        size="icon"
        className="min-touch text-destructive hover:text-destructive"
        aria-label="Удалить"
        title="Удалить"
        onClick={() => setDeleteOpen(true)}
      >
        <Trash2 size={16} />
      </Button>

      <DeleteConfirmDialog
        open={deleteOpen}
        onOpenChange={setDeleteOpen}
        title="Удалить тему?"
        description="Тему можно удалить только если к ней не привязаны банки вопросов."
        isPending={deleteMutation.isPending}
        onConfirm={() => deleteMutation.mutateAsync(topic.id)}
      />
    </div>
  );
}

function TopicTableRow({
  topic,
  isExpanded,
  onToggle,
  onEdit,
}: {
  topic: TrainerTopicAdmin;
  isExpanded: boolean;
  onToggle: () => void;
  onEdit: () => void;
}) {
  return (
    <>
      <TableRow className="cursor-pointer" onClick={onToggle}>
        <TableCell className="font-medium">
          <span className="flex items-center gap-2">
            <Icons.chevronRight
              size={14}
              className={cn(
                "shrink-0 text-muted-foreground/50 transition-transform",
                isExpanded && "rotate-90",
              )}
            />
            {topic.title}
          </span>
        </TableCell>
        <TableCell className="text-muted-foreground">{topic.area}</TableCell>
        <TableCell className="text-muted-foreground">
          {topic.direction ? TRAINER_DIRECTION_LABELS[topic.direction] ?? topic.direction : "—"}
        </TableCell>
        <TableCell>
          <StatusBadge status={topic.isPublished ? "PUBLISHED" : "DRAFT"} />
        </TableCell>
        <TableCell className="text-center tabular-nums">{topic.bankCount}</TableCell>
        <TableCell className="text-right" onClick={(e) => e.stopPropagation()}>
          <TopicActions topic={topic} onEdit={onEdit} />
        </TableCell>
      </TableRow>
      {isExpanded && (
        <TableRow className="hover:bg-transparent">
          <TableCell colSpan={6} className="bg-muted/20 p-4">
            <BanksPanel topicId={topic.id} />
          </TableCell>
        </TableRow>
      )}
    </>
  );
}

function TopicCard({
  topic,
  isExpanded,
  onToggle,
  onEdit,
}: {
  topic: TrainerTopicAdmin;
  isExpanded: boolean;
  onToggle: () => void;
  onEdit: () => void;
}) {
  return (
    <Card className="gap-0 overflow-hidden py-0">
      <CardContent className="flex flex-col gap-3 p-4">
        <button
          type="button"
          onClick={onToggle}
          className="flex w-full items-start gap-2 text-left"
          aria-expanded={isExpanded}
        >
          <Icons.chevronRight
            size={14}
            className={cn(
              "mt-1 shrink-0 text-muted-foreground/50 transition-transform",
              isExpanded && "rotate-90",
            )}
          />
          <span className="min-w-0 flex-1 space-y-1">
            <span className="block truncate text-sm font-semibold">{topic.title}</span>
            <span className="block truncate text-xs text-muted-foreground">{topic.area}</span>
            <span className="flex flex-wrap items-center gap-2 pt-0.5">
              <StatusBadge status={topic.isPublished ? "PUBLISHED" : "DRAFT"} />
              {topic.direction && (
                <span className="text-[11px] text-muted-foreground">
                  {TRAINER_DIRECTION_LABELS[topic.direction] ?? topic.direction}
                </span>
              )}
              <span className="text-[11px] tabular-nums text-muted-foreground">
                {topic.bankCount} банк(ов)
              </span>
            </span>
          </span>
        </button>

        <div className="border-t pt-3">
          <TopicActions topic={topic} onEdit={onEdit} />
        </div>

        {isExpanded && (
          <div className="border-t pt-3">
            <BanksPanel topicId={topic.id} />
          </div>
        )}
      </CardContent>
    </Card>
  );
}
