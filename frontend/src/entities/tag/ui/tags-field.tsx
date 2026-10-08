"use client";

import { useQuery } from "@tanstack/react-query";
import { Loader2, Plus, X } from "lucide-react";
import { useState } from "react";
import { useDebouncedCallback } from "use-debounce";
import { toast } from "sonner";
import { getErrorMessage } from "@/shared/api";
import type { EntityType } from "@/shared/config/entity-types";
import { cn } from "@/shared/lib/css";
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from "@/shared/ui/kit/popover";
import {
  Command,
  CommandEmpty,
  CommandGroup,
  CommandInput,
  CommandItem,
  CommandList,
} from "@/shared/ui/kit/command";
import { tagsApi, tagsQueryOptions } from "../api";
import { useAddTagsToEntity } from "../model/use-add-tags-to-entity";
import { useRemoveTagsFromEntity } from "../model/use-remove-tags-from-entity";
import type { TagDto } from "../types";

// --- Props ---

type ConnectedProps = {
  entityId: string;
  entityType: EntityType;
  readOnly?: boolean;
};

type ControlledProps = {
  value: string[];
  onChange: (titles: string[]) => void;
};

type TagsFieldProps = (ConnectedProps | ControlledProps) & {
  className?: string;
  onTagClick?: (tag: TagDto) => void;
};

function isConnected(
  props: TagsFieldProps,
): props is ConnectedProps & { className?: string; onTagClick?: (tag: TagDto) => void } {
  return "entityId" in props;
}

export function TagsField(props: TagsFieldProps) {
  if (isConnected(props)) {
    return <ConnectedTagsField {...props} />;
  }
  return <ControlledTagsField {...props} />;
}

// --- Connected Mode ---

function ConnectedTagsField({
  entityId,
  entityType,
  readOnly,
  className,
  onTagClick,
}: ConnectedProps & { className?: string; onTagClick?: (tag: TagDto) => void }) {
  const { addTagsToEntity } = useAddTagsToEntity();
  const { removeTagsFromEntity } = useRemoveTagsFromEntity();
  const [optimisticAdded, setOptimisticAdded] = useState<string[]>([]);
  const [optimisticRemoved, setOptimisticRemoved] = useState<string[]>([]);

  const { data: tags = [], isLoading } = useQuery(
    tagsQueryOptions.entityTags({ entityType, entityId }),
  );

  const displayTags: TagDto[] = [
    ...tags.filter((t) => !optimisticRemoved.includes(t.id)),
    ...optimisticAdded.map((title) => ({
      id: `optimistic:${title.toLowerCase()}`,
      title,
      slug: "",
      kind: "canon" as const,
    })),
  ];

  const selectedTitles = displayTags.map((t) => t.title.toLowerCase());
  const isBusy = optimisticAdded.length > 0 || optimisticRemoved.length > 0;

  async function handleAdd(title: string) {
    const normalized = title.trim();
    setOptimisticAdded((prev) => [...prev, normalized]);
    try {
      await addTagsToEntity({
        entityType,
        entityId,
        tagTitles: [normalized],
        tagIds: [],
      });
    } catch (error) {
      toast.error(getErrorMessage(error, "Ошибка добавления тега"));
    } finally {
      setOptimisticAdded((prev) => prev.filter((t) => t !== normalized));
    }
  }

  async function handleRemove(tag: TagDto) {
    if (tag.id.startsWith("optimistic:")) return;
    setOptimisticRemoved((prev) => [...prev, tag.id]);
    try {
      await removeTagsFromEntity({
        entityType,
        entityId,
        tagIds: [tag.id],
      });
    } catch (error) {
      toast.error(getErrorMessage(error, "Ошибка удаления тега"));
    } finally {
      setOptimisticRemoved((prev) => prev.filter((id) => id !== tag.id));
    }
  }

  if (isLoading) {
    return (
      <div
        className={cn(
          "flex items-center gap-2 text-sm text-muted-foreground",
          className,
        )}
      >
        <Loader2 className="size-3.5 animate-spin" />
      </div>
    );
  }

  if (readOnly && displayTags.length === 0) {
    return null;
  }

  return (
    <TagsDisplay
      tags={displayTags}
      readOnly={readOnly}
      disabled={isBusy}
      onRemove={handleRemove}
      onAdd={handleAdd}
      selectedTitles={selectedTitles}
      className={className}
      onTagClick={onTagClick}
    />
  );
}

// --- Controlled Mode ---

function ControlledTagsField({
  value,
  onChange,
  className,
  onTagClick,
}: ControlledProps & { className?: string; onTagClick?: (tag: TagDto) => void }) {
  const virtualTags: TagDto[] = value.map((title) => ({
    id: `local:${title.toLowerCase()}`,
    title,
    slug: "",
    kind: "canon",
  }));

  function handleAdd(title: string) {
    const normalized = title.trim().toLowerCase();
    if (value.some((t) => t.toLowerCase() === normalized)) return;
    onChange([...value, title.trim()]);
  }

  function handleRemove(tag: TagDto) {
    onChange(
      value.filter((t) => t.toLowerCase() !== tag.title.toLowerCase()),
    );
  }

  return (
    <TagsDisplay
      tags={virtualTags}
      readOnly={false}
      disabled={false}
      onRemove={handleRemove}
      onAdd={handleAdd}
      selectedTitles={value.map((t) => t.toLowerCase())}
      className={className}
      onTagClick={onTagClick}
    />
  );
}

// --- Shared Display ---

function TagsDisplay({
  tags,
  readOnly,
  disabled,
  onRemove,
  onAdd,
  selectedTitles,
  className,
  onTagClick,
}: {
  tags: TagDto[];
  readOnly?: boolean;
  disabled: boolean;
  onRemove: (tag: TagDto) => void;
  onAdd: (title: string) => void;
  selectedTitles: string[];
  className?: string;
  onTagClick?: (tag: TagDto) => void;
}) {
  return (
    <div
      className={cn(
        "flex flex-wrap items-center gap-2",
        className,
      )}
    >
      {tags.map((tag) =>
        readOnly && onTagClick ? (
          <button
            type="button"
            key={tag.id}
            onClick={() => onTagClick(tag)}
            className="inline-flex items-center rounded-full border border-border/50 bg-muted/40 px-3 py-1 text-sm font-medium text-muted-foreground transition-colors hover:border-primary/25 hover:bg-primary/10 hover:text-foreground"
          >
            {tag.title}
          </button>
        ) : readOnly ? (
          <span
            key={tag.id}
            className="inline-flex items-center rounded-full border border-border/50 bg-muted/40 px-3 py-1 text-sm font-medium text-muted-foreground"
          >
            {tag.title}
          </span>
        ) : (
          <span
            key={tag.id}
            className="group inline-flex items-center gap-1.5 rounded-full border border-border/50 bg-muted/40 px-3 py-1 text-sm font-medium text-muted-foreground transition-colors hover:border-border hover:bg-muted/60"
          >
            <span>{tag.title}</span>
            <button
              type="button"
              onClick={() => onRemove(tag)}
              disabled={disabled}
              className="inline-flex size-4 items-center justify-center rounded-full opacity-0 transition-opacity hover:bg-foreground/10 hover:text-foreground group-hover:opacity-100 disabled:pointer-events-none"
            >
              <X className="size-3" />
            </button>
          </span>
        ),
      )}
      {!readOnly && (
        <TagPickerPopover
          onSelect={onAdd}
          selectedTitles={selectedTitles}
          disabled={disabled}
        />
      )}
    </div>
  );
}

// --- Tag Picker Popover ---

function TagPickerPopover({
  onSelect,
  selectedTitles,
  disabled,
}: {
  onSelect: (title: string) => void;
  selectedTitles: string[];
  disabled: boolean;
}) {
  const [open, setOpen] = useState(false);
  const [search, setSearch] = useState("");
  const [debouncedSearch, setDebouncedSearch] = useState("");

  const debouncedSetSearch = useDebouncedCallback((value: string) => {
    setDebouncedSearch(value);
  }, 300);

  const { data: suggestions = [], isFetching } = useQuery({
    queryKey: [tagsQueryOptions.baseKey, "picker-suggest", debouncedSearch.trim()],
    queryFn: ({ signal }) =>
      tagsApi.suggestTags(
        { search: debouncedSearch.trim() || undefined, pageSize: 10 },
        { signal },
      ),
    select: (data) => data.result?.items ?? [],
    enabled: open,
  });

  const filteredSuggestions = suggestions.filter(
    (tag) => !selectedTitles.includes(tag.title.toLowerCase()),
  );

  const trimmed = search.trim();
  const canCreate =
    trimmed.length > 0 &&
    !selectedTitles.includes(trimmed.toLowerCase()) &&
    !suggestions.some((s) => s.title.toLowerCase() === trimmed.toLowerCase());

  function handleSelect(title: string) {
    onSelect(title);
    setSearch("");
    setDebouncedSearch("");
  }

  function handleOpenChange(nextOpen: boolean) {
    setOpen(nextOpen);
    if (!nextOpen) {
      setSearch("");
      setDebouncedSearch("");
    }
  }

  return (
    <Popover open={open} onOpenChange={handleOpenChange}>
      <PopoverTrigger asChild>
        <button
          type="button"
          disabled={disabled}
          className="inline-flex items-center gap-1.5 rounded-full border border-dashed border-border/50 px-3 py-1 text-sm font-medium text-muted-foreground/60 transition-colors hover:border-border hover:text-muted-foreground disabled:pointer-events-none disabled:opacity-50"
        >
          <Plus className="size-4" />
          <span>Тег</span>
        </button>
      </PopoverTrigger>
      <PopoverContent className="w-64 p-0" align="start">
        <Command shouldFilter={false}>
          <CommandInput
            placeholder="Поиск тегов..."
            value={search}
            onValueChange={(value) => {
              setSearch(value);
              debouncedSetSearch(value);
            }}
          />
          <CommandList>
            {isFetching ? (
              <div className="flex items-center justify-center py-4">
                <Loader2 className="size-4 animate-spin text-muted-foreground" />
              </div>
            ) : (
              <>
                {filteredSuggestions.length > 0 && (
                  <CommandGroup>
                    {filteredSuggestions.map((tag) => (
                      <CommandItem
                        key={tag.id}
                        onSelect={() => handleSelect(tag.title)}
                      >
                        {tag.title}
                      </CommandItem>
                    ))}
                  </CommandGroup>
                )}
                {filteredSuggestions.length === 0 && !canCreate && (
                  <CommandEmpty>
                    {trimmed
                      ? "Ничего не найдено"
                      : "Начните вводить для поиска"}
                  </CommandEmpty>
                )}
                {canCreate && (
                  <CommandGroup>
                    <CommandItem onSelect={() => handleSelect(trimmed)}>
                      <Plus className="size-3.5" />
                      Создать «{trimmed}»
                    </CommandItem>
                  </CommandGroup>
                )}
              </>
            )}
          </CommandList>
        </Command>
      </PopoverContent>
    </Popover>
  );
}
