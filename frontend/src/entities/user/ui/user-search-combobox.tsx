"use client";

import { UserAvatar } from "@/shared/ui/components";
import { Badge } from "@/shared/ui/kit/badge";
import { Input } from "@/shared/ui/kit/input";
import { cn } from "@/shared/lib/css";
import { useInfiniteScroll } from "@/shared/hooks";
import { useInfiniteQuery } from "@tanstack/react-query";
import { Check, Loader2, Search, X } from "lucide-react";
import { useState } from "react";
import { useDebouncedCallback } from "use-debounce";
import { usersQueryOptions } from "../api";
import type { AdminUserSummary } from "../types";

export interface UserSearchPickerProps {
  selectedUsers: AdminUserSummary[];
  onSelectedUsersChange: (users: AdminUserSummary[]) => void;
  excludeUserIds?: string[];
  placeholder?: string;
  /** Limit selection to one user at a time */
  singleSelect?: boolean;
  /** Hide the selected chips above the search (caller renders its own UI) */
  hideSelectedChips?: boolean;
  /** Max height of the results list. Default: 15rem */
  listMaxHeight?: string;
  /** Show all users on mount instead of waiting for input */
  showAllByDefault?: boolean;
  /** Restrict results to users having this platform role (e.g. "platform-author"). */
  role?: string;
}

/**
 * Unified user search & selection component.
 *
 * Features:
 *  - Inline search input (no popover click-through)
 *  - Infinite-scroll pagination (loads 20 at a time)
 *  - Multi-select with chips above the search
 *  - Colors match surrounding card (no dark popover mismatch)
 *
 * Used anywhere we need to pick platform users.
 */
export function UserSearchCombobox({
  selectedUsers,
  onSelectedUsersChange,
  excludeUserIds = [],
  placeholder = "Имя, username или Telegram...",
  singleSelect = false,
  hideSelectedChips = false,
  listMaxHeight = "15rem",
  showAllByDefault = false,
  role,
}: UserSearchPickerProps) {
  const [search, setSearch] = useState("");
  const [debouncedSearch, setDebouncedSearch] = useState("");

  const debouncedSetSearch = useDebouncedCallback((value: string) => {
    setDebouncedSearch(value);
  }, 300);

  const shouldFetch = showAllByDefault || debouncedSearch.length >= 1;

  const query = useInfiniteQuery({
    ...usersQueryOptions.searchUsersInfiniteOptions(debouncedSearch, { role }),
    enabled: shouldFetch,
  });

  const setCursorRef = useInfiniteScroll({
    hasNextPage: query.hasNextPage,
    isFetchingNextPage: query.isFetchingNextPage,
    fetchNextPage: () => {
      void query.fetchNextPage();
    },
  });

  const selectedIds = new Set(selectedUsers.map((u) => u.id));
  const excludeIds = new Set(excludeUserIds);

  const items = (query.data?.items ?? []).filter((u) => !excludeIds.has(u.id));

  const toggleUser = (user: AdminUserSummary) => {
    if (selectedIds.has(user.id)) {
      onSelectedUsersChange(selectedUsers.filter((u) => u.id !== user.id));
      return;
    }
    if (singleSelect) {
      onSelectedUsersChange([user]);
      return;
    }
    onSelectedUsersChange([...selectedUsers, user]);
  };

  const removeUser = (userId: string) => {
    onSelectedUsersChange(selectedUsers.filter((u) => u.id !== userId));
  };

  const onSearchChange = (value: string) => {
    setSearch(value);
    debouncedSetSearch(value.trim());
  };

  const isInitialLoad = query.isLoading && shouldFetch;
  const showList = shouldFetch;

  return (
    <div className="space-y-3">
      {!hideSelectedChips && selectedUsers.length > 0 && (
        <div className="flex flex-wrap gap-1.5">
          {selectedUsers.map((user) => (
            <Badge key={user.id} variant="secondary" className="gap-1 pr-1">
              <UserAvatar
                name={user.displayName || user.userName}
                avatarId={user.avatarId}
                className="size-4"
              />
              <span className="max-w-[140px] truncate">
                {user.displayName?.trim() || user.userName || user.email}
              </span>
              <button
                type="button"
                className="rounded-full p-0.5 hover:bg-muted-foreground/20"
                onClick={() => removeUser(user.id)}
                aria-label={`Удалить ${user.userName ?? user.email}`}
              >
                <X className="size-3" />
              </button>
            </Badge>
          ))}
        </div>
      )}

      <div className="relative">
        <Search className="absolute left-3 top-1/2 -translate-y-1/2 size-4 text-muted-foreground pointer-events-none" />
        <Input
          value={search}
          onChange={(e) => onSearchChange(e.target.value)}
          placeholder={placeholder}
          className="pl-9"
        />
      </div>

      {showList && (
        <div
          className="rounded-md border bg-card overflow-y-auto"
          style={{ maxHeight: listMaxHeight }}
        >
          {isInitialLoad ? (
            <div className="flex items-center justify-center py-8">
              <Loader2 className="size-4 animate-spin text-muted-foreground" />
            </div>
          ) : items.length === 0 ? (
            <div className="py-6 text-center text-sm text-muted-foreground">
              {debouncedSearch ? "Пользователи не найдены" : "Нет пользователей"}
            </div>
          ) : (
            <ul className="divide-y">
              {items.map((user) => {
                const isSelected = selectedIds.has(user.id);
                return (
                  <li key={user.id}>
                    <button
                      type="button"
                      onClick={() => toggleUser(user)}
                      className={cn(
                        "flex w-full items-center gap-2 px-3 py-2 text-left transition-colors",
                        isSelected
                          ? "bg-accent/50 hover:bg-accent/70"
                          : "hover:bg-accent/40",
                      )}
                    >
                      <UserAvatar
                        name={user.displayName || user.userName}
                        avatarId={user.avatarId}
                        className="size-7 shrink-0"
                      />
                      <div className="min-w-0 flex-1">
                        <p className="text-sm font-medium truncate">
                          {user.displayName?.trim() || user.userName || "Без имени"}
                        </p>
                        <p className="text-xs text-muted-foreground truncate">
                          {user.userName && `@${user.userName}`}
                          {user.userName && user.email && " \u00b7 "}
                          {user.email}
                        </p>
                      </div>
                      <Check
                        className={cn(
                          "size-4 shrink-0 text-primary",
                          isSelected ? "opacity-100" : "opacity-0",
                        )}
                      />
                    </button>
                  </li>
                );
              })}
            </ul>
          )}
          {items.length > 0 && (
            <div ref={setCursorRef} className="h-1" />
          )}
          {query.isFetchingNextPage && (
            <div className="flex justify-center py-2">
              <Loader2 className="size-4 animate-spin text-muted-foreground" />
            </div>
          )}
        </div>
      )}
    </div>
  );
}
