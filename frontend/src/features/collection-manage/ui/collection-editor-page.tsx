"use client";

import {
  collectionDetailQueryOptions,
  type CollectionAccessType,
  type CollectionDetailDto,
  type CollectionSectionDto,
  type CollectionItemDto,
} from "@/entities/collection";
import { getMaterialKindBadge, MaterialPickerDialog } from "@/entities/material";
import { QuizPickerDialog } from "@/entities/quiz";
import { cn } from "@/shared/lib/css";
import { pluralize } from "@/shared/lib/pluralize";
import { Icons } from "@/shared/ui/icons";
import { AccessTypeSelector } from "@/shared/ui/components/access-type-selector";
import { NotFoundFallback } from "@/shared/ui/components/not-found-fallback";
import { Badge } from "@/shared/ui/kit/badge";
import { Button } from "@/shared/ui/kit/button";
import { ErrorCard } from "@/shared/ui/kit/error-card";
import { Input } from "@/shared/ui/kit/input";
import { Label } from "@/shared/ui/kit/label";
import { Textarea } from "@/shared/ui/kit/textarea";
import { useQuery } from "@tanstack/react-query";
import { ChevronDown, Loader2, Pencil, Plus, Save, ShieldCheck, Trash2 } from "lucide-react";
import { routes } from "@/shared/config/routes";
import Link from "next/link";
import { useState } from "react";
import { CollectionAuthorActions } from "./collection-author-actions";
import { BulkAccessTypeDialog } from "./bulk-access-type-dialog";
import { PreviewUpload } from "@/shared/ui/components";
import { useUpdateCollection } from "../model/use-update-collection";
import { useUploadCollectionCover } from "../model/use-upload-collection-cover";
import { useAddSection } from "../model/use-add-section";
import { useUpdateSection } from "../model/use-update-section";
import { useRemoveSection } from "../model/use-remove-section";
import { useAddItem } from "../model/use-add-item";
import { useRemoveItem } from "../model/use-remove-item";

interface CollectionEditorPageProps {
  collectionId: string;
}

const STATUS_BADGES: Record<string, { label: string; className: string }> = {
  DRAFT: {
    label: "Черновик",
    className: "border-yellow/30 bg-yellow/10 text-yellow",
  },
  PUBLISHED: {
    label: "Опубликована",
    className: "border-border/50 bg-transparent text-muted-foreground/60",
  },
  ARCHIVED: {
    label: "Архив",
    className: "border-border bg-secondary text-muted-foreground",
  },
};

export function CollectionEditorPage({ collectionId }: CollectionEditorPageProps) {
  const {
    data: collection,
    isLoading,
    error,
  } = useQuery(collectionDetailQueryOptions(collectionId));

  if (isLoading) {
    return (
      <div className="flex justify-center py-16">
        <Loader2 className="size-6 animate-spin text-muted-foreground" />
      </div>
    );
  }

  if (error) {
    return <ErrorCard error={error} className="py-16" />;
  }

  if (!collection) {
    return (
      <NotFoundFallback
        message="Подборка не найдена"
        backHref={routes.authorCollections}
        backLabel="К списку подборок"
      />
    );
  }

  return <CollectionEditorInner collection={collection} />;
}

function CollectionEditorInner({ collection }: { collection: CollectionDetailDto }) {
  const [title, setTitle] = useState(collection.title);
  const [description, setDescription] = useState(collection.description ?? "");
  const [accessType, setAccessType] = useState<CollectionAccessType>(collection.accessType);
  const [bulkAccessOpen, setBulkAccessOpen] = useState(false);
  const { updateCollection, isPending: isUpdating } = useUpdateCollection();
  const { addSection, isPending: isAddingSec } = useAddSection(collection.id);
  const coverUpload = useUploadCollectionCover(collection.id);

  const hasAnyItem = collection.sections.some((s) => s.items.length > 0);

  const statusBadge = STATUS_BADGES[collection.status];

  const handleSave = async () => {
    // Resolve current cover asset id: a freshly uploaded one wins, otherwise
    // fall back to the existing collection cover. Pass through to the backend
    // so it can do a sync bind/detach in the same transaction as the save.
    const coverId =
      coverUpload.uploadedAssetId ??
      (coverUpload.isDeleted ? null : (collection.coverImageId ?? null));

    await updateCollection({
      id: collection.id,
      request: {
        title,
        description: description || null,
        accessType,
        coverId,
      },
    });
  };

  const handleAddSection = async () => {
    await addSection({
      collectionId: collection.id,
      request: { title: null, description: null },
    });
  };

  return (
    <div className="min-h-full bg-background">
      {/* Header bar */}
      <div className="border-b border-border/70 px-4 py-4 md:px-6">
        <div className="mx-auto flex w-full max-w-5xl flex-col gap-3 md:flex-row md:items-center md:justify-between md:gap-4">
          <div className="min-w-0 space-y-1">
            <Link
              href={
                collection.courseSlug
                  ? routes.authorCourseBuilder(collection.courseSlug)
                  : routes.authorCollections
              }
              className="inline-flex text-sm text-muted-foreground transition-colors hover:text-foreground"
            >
              &larr; {collection.courseId ? "Назад к курсу" : "Назад к подборкам"}
            </Link>
            <div className="flex flex-wrap items-center gap-2 md:gap-3">
              <h1 className="text-lg font-bold md:text-xl">Редактирование подборки</h1>
              {statusBadge && (
                <Badge variant="outline" className={statusBadge.className}>
                  {statusBadge.label}
                </Badge>
              )}
            </div>
          </div>

          <div className="flex items-center gap-2 self-end shrink-0 md:self-auto">
            <CollectionAuthorActions collection={collection} context="editor" />
            <Button onClick={() => void handleSave()} disabled={isUpdating || !title.trim()}>
              {isUpdating ? <Loader2 size={14} className="animate-spin" /> : <Save size={14} />}
              {isUpdating ? "Сохранение..." : "Сохранить"}
            </Button>
          </div>
        </div>
      </div>

      {/* Form */}
      <div className="mx-auto max-w-5xl p-4 md:p-6 space-y-8">
        {/* Metadata */}
        <div className="space-y-4">
          <div className="space-y-2">
            <Label>Обложка</Label>
            <PreviewUpload
              hook={coverUpload}
              imageId={collection.coverImageId}
              initialPreviewUrl={collection.coverImageUrl}
              alt="Обложка подборки"
              aspectRatio="portrait"
            />
          </div>
          <div className="space-y-2">
            <Label htmlFor="collection-title">Название</Label>
            <Input
              id="collection-title"
              value={title}
              onChange={(e) => setTitle(e.target.value)}
              placeholder="Название подборки"
            />
          </div>
          <div className="space-y-2">
            <Label htmlFor="collection-description">Описание</Label>
            <Textarea
              id="collection-description"
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              placeholder="Краткое описание подборки (необязательно)"
              rows={3}
            />
          </div>
          <div className="space-y-2">
            <Label>Уровень доступа</Label>
            <AccessTypeSelector
              value={accessType}
              onChange={(v) => setAccessType(v as CollectionAccessType)}
              hasCourseBinding={!!collection.courseId}
            />
          </div>
        </div>

        {/* Sections */}
        <div className="space-y-4">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <h2 className="text-lg font-semibold">Секции</h2>
            <div className="flex flex-wrap items-center gap-2">
              <Button
                variant="outline"
                size="sm"
                onClick={() => setBulkAccessOpen(true)}
                disabled={!hasAnyItem}
                title={
                  hasAnyItem
                    ? "Сменить уровень доступа сразу у всех материалов подборки"
                    : "В подборке нет материалов"
                }
              >
                <ShieldCheck size={14} />
                Доступ у всех материалов
              </Button>
              <Button
                variant="outline"
                size="sm"
                onClick={() => void handleAddSection()}
                disabled={isAddingSec}
              >
                {isAddingSec ? <Loader2 size={14} className="animate-spin" /> : <Plus size={14} />}
                Добавить секцию
              </Button>
            </div>
          </div>

          {collection.sections.length === 0 ? (
            <div className="rounded-xl border border-dashed border-border/70 bg-card/70 px-6 py-10 text-center text-muted-foreground">
              <p className="font-medium">Нет секций</p>
              <p className="mt-1 text-sm">Добавьте секцию, чтобы начать наполнять подборку</p>
            </div>
          ) : (
            <div className="space-y-4">
              {collection.sections.map((section, idx) => (
                <SectionEditor
                  key={section.id}
                  section={section}
                  collectionId={collection.id}
                  courseId={collection.courseId}
                  sectionIndex={idx}
                />
              ))}
            </div>
          )}
        </div>
      </div>

      <BulkAccessTypeDialog
        open={bulkAccessOpen}
        onOpenChange={setBulkAccessOpen}
        collectionId={collection.id}
        currentAccessType={collection.accessType}
        hasCourseBinding={!!collection.courseId}
      />
    </div>
  );
}

function SectionEditor({
  section,
  collectionId,
  courseId,
  sectionIndex,
}: {
  section: CollectionSectionDto;
  collectionId: string;
  courseId: string | null;
  sectionIndex: number;
}) {
  const [isEditing, setIsEditing] = useState(false);
  const [isCollapsed, setIsCollapsed] = useState(false);
  const [sectionTitle, setSectionTitle] = useState(section.title ?? "");
  const [sectionDesc, setSectionDesc] = useState(section.description ?? "");
  const [materialPickerOpen, setMaterialPickerOpen] = useState(false);
  const [quizPickerOpen, setQuizPickerOpen] = useState(false);

  const { updateSection, isPending: isUpdatingSec } = useUpdateSection(collectionId);
  const { removeSection, isPending: isRemovingSec } = useRemoveSection(collectionId);
  const { removeItem, isPending: isRemovingItem } = useRemoveItem(collectionId);
  const { addItem, isPending: isAddingItem } = useAddItem(collectionId);

  const handleSaveSection = async () => {
    await updateSection({
      collectionId,
      sectionId: section.id,
      request: {
        title: sectionTitle || null,
        description: sectionDesc || null,
      },
    });
    setIsEditing(false);
  };

  const handleRemoveSection = async () => {
    await removeSection({ collectionId, sectionId: section.id });
  };

  const handleRemoveItem = async (itemId: string) => {
    await removeItem({ collectionId, sectionId: section.id, itemId });
  };

  return (
    <div className="rounded-2xl border bg-card text-card-foreground shadow-sm overflow-hidden">
      {/* Section header */}
      {isEditing ? (
        <div className="px-4 py-3 space-y-2 border-b">
          <Input
            value={sectionTitle}
            onChange={(e) => setSectionTitle(e.target.value)}
            placeholder="Название секции (необязательно)"
            className="text-sm"
          />
          <Input
            value={sectionDesc}
            onChange={(e) => setSectionDesc(e.target.value)}
            placeholder="Описание секции (необязательно)"
            className="text-sm"
          />
          <div className="flex gap-2">
            <Button
              size="sm"
              variant="outline"
              onClick={() => void handleSaveSection()}
              disabled={isUpdatingSec}
            >
              {isUpdatingSec ? <Loader2 size={12} className="animate-spin" /> : "Сохранить"}
            </Button>
            <Button size="sm" variant="ghost" onClick={() => setIsEditing(false)}>
              Отмена
            </Button>
          </div>
        </div>
      ) : (
        <div
          role="button"
          tabIndex={0}
          className="group flex w-full items-center gap-2 sm:gap-3 px-3 sm:px-5 py-3 sm:py-4 text-left cursor-pointer"
          onClick={() => setIsCollapsed((v) => !v)}
          onKeyDown={(e) => {
            if (e.key === "Enter" || e.key === " ") {
              e.preventDefault();
              setIsCollapsed((v) => !v);
            }
          }}
        >
          <span className="text-xs font-bold tabular-nums text-muted-foreground/50 w-5 text-right shrink-0">
            {sectionIndex + 1}
          </span>
          <div className="flex-1 min-w-0">
            <div className="flex items-center gap-2">
              <span className="text-sm font-semibold truncate">
                {section.title || `Секция ${sectionIndex + 1}`}
              </span>
              <span className="text-[10px] tabular-nums text-muted-foreground/60 bg-muted rounded-full px-1.5 py-px shrink-0">
                {section.items.length}
              </span>
            </div>
          </div>
          <div
            className="flex items-center gap-1 opacity-0 group-hover:opacity-100 transition-opacity"
            onClick={(e) => e.stopPropagation()}
          >
            <Button
              variant="ghost"
              size="icon"
              className="size-7 text-muted-foreground hover:text-foreground"
              aria-label="Редактировать секцию"
              onClick={() => setIsEditing(true)}
            >
              <Pencil size={14} />
            </Button>
            <Button
              variant="ghost"
              size="icon"
              className="size-7 text-destructive hover:text-destructive"
              aria-label="Удалить секцию"
              onClick={() => void handleRemoveSection()}
              disabled={isRemovingSec}
            >
              {isRemovingSec ? (
                <Loader2 size={14} className="animate-spin" />
              ) : (
                <Trash2 size={14} />
              )}
            </Button>
          </div>
          <ChevronDown
            size={14}
            className={cn(
              "text-muted-foreground/40 shrink-0 transition-transform",
              isCollapsed && "-rotate-90",
            )}
          />
        </div>
      )}

      {/* Section items */}
      {!isCollapsed && (
        <div
          className={cn(
            "border-t px-3 transition-colors",
            section.items.length > 0 ? "py-2" : "py-0",
          )}
        >
          {section.items.length > 0 ? (
            <div className="space-y-0.5">
              {section.items.map((item) => (
                <SectionItemRow
                  key={item.id}
                  item={item}
                  collectionId={collectionId}
                  onRemove={() => void handleRemoveItem(item.id)}
                  isRemoving={isRemovingItem}
                />
              ))}
            </div>
          ) : (
            <div className="text-xs text-muted-foreground/50 text-center py-3">Пусто</div>
          )}

          <div className="flex gap-2 py-2">
            <Button
              variant="outline"
              size="sm"
              className="flex-1"
              onClick={() => setMaterialPickerOpen(true)}
            >
              <Plus size={14} />
              Добавить материал
            </Button>
            <Button
              variant="outline"
              size="sm"
              className="flex-1"
              onClick={() => setQuizPickerOpen(true)}
            >
              <Icons.quiz size={14} />
              Добавить тест
            </Button>
          </div>
        </div>
      )}

      <MaterialPickerDialog
        open={materialPickerOpen}
        onOpenChange={setMaterialPickerOpen}
        title="Добавить материал"
        description="Выберите материал для добавления в секцию"
        courseId={courseId ?? undefined}
        collectionId={collectionId}
        sectionId={section.id}
        onSelect={(materialId) => {
          void addItem({
            collectionId,
            sectionId: section.id,
            request: { referenceId: materialId, itemType: "MATERIAL" },
          });
        }}
        isPending={isAddingItem}
        excludedIds={
          new Set(
            section.items.filter((i) => i.itemType !== "QUIZ").map((i) => i.referenceId),
          )
        }
      />

      <QuizPickerDialog
        open={quizPickerOpen}
        onOpenChange={setQuizPickerOpen}
        title="Добавить тест"
        description="Выберите тест из библиотеки для добавления в секцию"
        onSelect={(quizId) => {
          void addItem({
            collectionId,
            sectionId: section.id,
            request: { referenceId: quizId, itemType: "QUIZ" },
          });
          setQuizPickerOpen(false);
        }}
        isPending={isAddingItem}
        excludedIds={
          new Set(
            section.items.filter((i) => i.itemType === "QUIZ").map((i) => i.referenceId),
          )
        }
      />
    </div>
  );
}

function SectionItemRow({
  item,
  collectionId,
  onRemove,
  isRemoving,
}: {
  item: CollectionItemDto;
  collectionId: string;
  onRemove: () => void;
  isRemoving: boolean;
}) {
  const isQuiz = item.itemType === "QUIZ";

  // #498: переход в той же вкладке (SPA — владелец: «всё в одной вкладке»).
  // Материал — его страница (`collectionId` в query даёт «Назад к подборке»
  // в header'е — путь возврата сохраняется и без второй вкладки); квиз —
  // библиотека /author/quizzes с авто-раскрытым редактором (#494).
  const editHref = isQuiz
    ? routes.authorQuizEdit(item.referenceId)
    : routes.authorMaterialEdit(item.referenceId, { collectionId });

  const kindBadge = item.material ? getMaterialKindBadge(item.material.kind) : null;

  return (
    <Link
      href={editHref}
      className="group/item flex items-center gap-2 sm:gap-3 px-2 sm:px-3 py-2.5 rounded-lg hover:bg-accent/30 transition-colors cursor-pointer"
    >
      {/* Kind icon */}
      {isQuiz ? (
        <div className="flex size-6 shrink-0 items-center justify-center rounded-full bg-violet-500/15">
          <Icons.quiz size={12} className="text-violet-500" />
        </div>
      ) : (
        kindBadge && (
          <div
            className={cn(
              "flex size-6 shrink-0 items-center justify-center rounded-full",
              kindBadge.iconBgClassName,
            )}
          >
            <kindBadge.icon size={12} />
          </div>
        )
      )}

      {/* Title */}
      <span className="text-sm truncate flex-1 min-w-0">
        {isQuiz ? (item.quizTitle ?? "Тест") : item.material?.title}
      </span>

      {/* Questions count (quiz only) */}
      {isQuiz && item.questionsCount != null && (
        <span className="text-[10px] tabular-nums text-muted-foreground/70 shrink-0">
          {item.questionsCount} {pluralize(item.questionsCount, "вопрос", "вопроса", "вопросов")}
        </span>
      )}

      {/* ItemType / kind badge */}
      {isQuiz ? (
        <span className="text-[9px] rounded-[3px] px-1.5 py-px shrink-0 border border-violet-500/30 bg-violet-500/10 text-violet-400">
          Тест
        </span>
      ) : (
        kindBadge && (
          <span
            className={cn("text-[9px] rounded-[3px] px-1.5 py-px shrink-0", kindBadge.className)}
          >
            {kindBadge.label}
          </span>
        )
      )}

      {/* Remove button */}
      <Button
        variant="ghost"
        size="icon"
        className="size-7 text-destructive hover:text-destructive shrink-0 opacity-0 group-hover/item:opacity-100 transition-opacity"
        aria-label={isQuiz ? "Удалить тест из секции" : "Удалить материал"}
        onClick={(event) => {
          // Останавливаем переход к редактору — кнопка вложена в Link.
          event.preventDefault();
          event.stopPropagation();
          onRemove();
        }}
        disabled={isRemoving}
      >
        <Trash2 size={14} />
      </Button>
    </Link>
  );
}
