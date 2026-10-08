"use client";

import { tagsQueryOptions, type TagDto } from "@/entities/tag";
import { cn } from "@/shared/lib/css";
import { Icons } from "@/shared/ui/icons";
import {
  Command,
  CommandEmpty,
  CommandGroup,
  CommandInput,
  CommandItem,
  CommandList,
} from "@/shared/ui/kit/command";
import { Popover, PopoverContent, PopoverTrigger } from "@/shared/ui/kit/popover";
import { useQuery } from "@tanstack/react-query";
import { useRef, useState } from "react";
import { useDebouncedCallback } from "use-debounce";

interface SearchTagPickerProps {
  selectedTags: TagDto[];
  onRemove: (tagId: string) => void;
  onAdd: (tag: TagDto) => void;
  className?: string;
  suggestionTags?: TagDto[];
  isSuggestionTagsLoading?: boolean;
  suggestionAuthorId?: string;
}

export function SearchTagPicker({
  selectedTags,
  onRemove,
  onAdd,
  className,
  suggestionTags,
  isSuggestionTagsLoading = false,
  suggestionAuthorId,
}: SearchTagPickerProps) {
  const [open, setOpen] = useState(false);
  const [search, setSearch] = useState("");
  const [debouncedSearch, setDebouncedSearch] = useState("");
  const inputRef = useRef<HTMLInputElement | null>(null);
  const focusFrameRef = useRef<number | null>(null);

  const debouncedSetSearch = useDebouncedCallback((value: string) => {
    setDebouncedSearch(value);
  }, 250);

  function handleInputRef(node: HTMLInputElement | null) {
    inputRef.current = node;

    if (focusFrameRef.current !== null) {
      window.cancelAnimationFrame(focusFrameRef.current);
      focusFrameRef.current = null;
    }

    if (node && open) {
      focusFrameRef.current = window.requestAnimationFrame(() => {
        focusFrameRef.current = null;
        node.focus();
      });
    }
  }

  const selectedTitles = selectedTags.map((tag) => tag.title.toLowerCase());
  const trimmedDebouncedSearch = debouncedSearch.trim();
  const usesScopedSuggestions = suggestionTags !== undefined;
  const suggestionsQuery = useQuery({
    ...tagsQueryOptions.suggest({
      search: trimmedDebouncedSearch || undefined,
      pageSize: 10,
      authorId: suggestionAuthorId,
    }),
    enabled: !usesScopedSuggestions && trimmedDebouncedSearch.length > 0,
  });

  const query = trimmedDebouncedSearch.toLowerCase();
  const scopedSuggestions = (suggestionTags ?? []).filter((tag) =>
    tag.title.toLowerCase().includes(query),
  );
  const displayedSuggestions =
    trimmedDebouncedSearch.length > 0
      ? (usesScopedSuggestions ? scopedSuggestions : (suggestionsQuery.data ?? [])).filter(
          (tag) => !selectedTitles.includes(tag.title.toLowerCase()),
        )
      : [];
  const isLoadingSuggestions =
    trimmedDebouncedSearch.length > 0 &&
    (usesScopedSuggestions ? isSuggestionTagsLoading : suggestionsQuery.isFetching);

  function handleSelect(tag: TagDto) {
    onAdd(tag);
    setSearch("");
    setDebouncedSearch("");
    setOpen(false);
  }

  return (
    <div className={cn("space-y-3", className)}>
      {selectedTags.length > 0 && (
        <div className="flex flex-wrap items-center gap-2">
          {selectedTags.map((tag) => (
            <button
              type="button"
              key={tag.id}
              onClick={() => onRemove(tag.id)}
              className="group inline-flex items-center gap-1.5 rounded-full border border-teal/25 bg-teal/10 px-3 py-1 text-[13px] font-medium text-foreground transition-colors hover:border-teal/40 hover:bg-teal/15 md:text-sm"
              aria-label={`Убрать тег ${tag.title}`}
            >
              <span>{tag.title}</span>
              <span className="inline-flex size-4 items-center justify-center rounded-full bg-primary/10 text-primary transition-transform group-hover:scale-105">
                <Icons.close className="size-3" />
              </span>
            </button>
          ))}
        </div>
      )}

      <div className="flex flex-wrap items-center gap-2">
        <Popover
          open={open}
          onOpenChange={(nextOpen) => {
            setOpen(nextOpen);
            if (!nextOpen) {
              setSearch("");
              setDebouncedSearch("");
            }
          }}
        >
          <PopoverTrigger asChild>
            <button
              type="button"
              className={cn(
                "inline-flex items-center gap-1.5 rounded-full border px-3 py-1 text-[13px] font-medium transition-colors md:text-sm",
                open
                  ? "border-teal/30 bg-teal/10 text-primary"
                  : "border-dashed border-border/50 text-muted-foreground hover:border-border hover:text-foreground",
              )}
            >
              <Icons.add className="size-4" />
              <span>Тег</span>
            </button>
          </PopoverTrigger>
          <PopoverContent
            data-global-search-tag-picker="true"
            align="start"
            sideOffset={8}
            collisionPadding={16}
            avoidCollisions
            className="z-[60] w-[min(380px,calc(100vw-2rem))] max-h-[min(360px,60vh)] overflow-hidden rounded-[1.1rem] border-border/70 bg-popover/98 p-0 shadow-[0_20px_50px_-30px_rgba(0,0,0,0.8)]"
          >
            <Command shouldFilter={false}>
              <CommandInput
                ref={handleInputRef}
                placeholder="Поиск тегов..."
                value={search}
                onValueChange={(value) => {
                  setSearch(value);
                  debouncedSetSearch(value);
                }}
              />
              <CommandList className="max-h-[min(260px,45vh)]">
                {isLoadingSuggestions ? (
                  <div className="flex items-center justify-center py-4">
                    <Icons.loading className="size-4 animate-spin text-muted-foreground" />
                  </div>
                ) : displayedSuggestions.length === 0 ? (
                  <CommandEmpty>
                    {trimmedDebouncedSearch.length > 0 ? "Ничего не найдено" : "Введите тег"}
                  </CommandEmpty>
                ) : (
                  <CommandGroup>
                    {displayedSuggestions.map((tag) => (
                      <CommandItem
                        key={tag.id}
                        onSelect={() => handleSelect(tag)}
                        className="mx-2 my-1 rounded-xl"
                      >
                        {tag.title}
                      </CommandItem>
                    ))}
                  </CommandGroup>
                )}
              </CommandList>
            </Command>
          </PopoverContent>
        </Popover>
      </div>
    </div>
  );
}
