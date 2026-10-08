"use client";

import { userLookupQueryOptions, type UserLookupResultDto } from "@/entities/access-plan";
import { useDebouncedValue } from "@/shared/hooks/use-debounced-value";
import { Avatar, AvatarFallback, AvatarImage } from "@/shared/ui/kit/avatar";
import { Button } from "@/shared/ui/kit/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/shared/ui/kit/dialog";
import { Input } from "@/shared/ui/kit/input";
import { Label } from "@/shared/ui/kit/label";
import { Icons } from "@/shared/ui/icons";
import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { useAdminGrant } from "../model/use-author-plans";

interface GrantUserDialogProps {
  planId: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

/**
 * Author/admin диалог «выдать grant пользователю». Поток:
 *  1. ввод имени/username/Telegram → debounce 300ms → GET /access/users/lookup
 *  2. клик на юзера → preselect
 *  3. submit → POST /access/grants/admin/
 */
export function GrantUserDialog({ planId, open, onOpenChange }: GrantUserDialogProps) {
  const [query, setQuery] = useState("");
  const debouncedQuery = useDebouncedValue(query.trim(), 300);
  const [selected, setSelected] = useState<UserLookupResultDto | null>(null);

  const lookupQuery = useQuery(userLookupQueryOptions(debouncedQuery));
  const grant = useAdminGrant(planId);

  const handleClose = () => {
    setQuery("");
    setSelected(null);
    onOpenChange(false);
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!selected) return;
    await grant.mutateAsync({ planId, userId: selected.userId });
    handleClose();
  };

  const results = lookupQuery.data ?? [];

  return (
    <Dialog open={open} onOpenChange={(o) => (o ? onOpenChange(o) : handleClose())}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Выдать доступ</DialogTitle>
          <DialogDescription>
            Найдите пользователя по имени, username или Telegram и выдайте ему grant на этот план.
          </DialogDescription>
        </DialogHeader>

        <form onSubmit={handleSubmit} className="space-y-4">
          <div className="space-y-1.5">
            <Label htmlFor="user-search" className="text-xs">
              Имя, username или Telegram
            </Label>
            <Input
              id="user-search"
              autoFocus
              autoComplete="off"
              value={query}
              onChange={(e) => {
                setQuery(e.target.value);
                if (selected) setSelected(null);
              }}
              placeholder="например, ivan или @ivan"
            />
          </div>

          {selected ? (
            <SelectedUser user={selected} onClear={() => setSelected(null)} />
          ) : (
            <UserResultsList
              results={results}
              isLoading={lookupQuery.isFetching}
              query={debouncedQuery}
              onSelect={setSelected}
            />
          )}

          <DialogFooter>
            <Button type="button" variant="ghost" onClick={handleClose}>
              Отмена
            </Button>
            <Button type="submit" disabled={!selected || grant.isPending}>
              {grant.isPending && <Icons.loading className="size-4 animate-spin" />}
              Выдать доступ
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

function SelectedUser({
  user,
  onClear,
}: {
  user: UserLookupResultDto;
  onClear: () => void;
}) {
  return (
    <div className="flex items-center gap-3 rounded-lg border border-primary/30 bg-primary/5 p-3">
      <UserAvatar user={user} />
      <div className="min-w-0 flex-1">
        <p className="text-sm font-medium truncate">{userTitle(user)}</p>
        <p className="text-xs text-muted-foreground truncate">{user.email}</p>
      </div>
      <Button type="button" size="sm" variant="ghost" onClick={onClear}>
        <Icons.close className="size-4" />
      </Button>
    </div>
  );
}

function UserResultsList({
  results,
  isLoading,
  query,
  onSelect,
}: {
  results: UserLookupResultDto[];
  isLoading: boolean;
  query: string;
  onSelect: (user: UserLookupResultDto) => void;
}) {
  if (query.length < 2) {
    return (
      <p className="text-xs text-muted-foreground">Введите минимум 2 символа для поиска</p>
    );
  }
  if (isLoading) {
    return (
      <div className="flex items-center justify-center py-4">
        <Icons.loading className="size-4 animate-spin text-muted-foreground" />
      </div>
    );
  }
  if (results.length === 0) {
    return <p className="text-xs text-muted-foreground">Никого не нашли по «{query}»</p>;
  }
  return (
    <ul className="max-h-64 space-y-1 overflow-y-auto">
      {results.map((user) => (
        <li key={user.userId}>
          <button
            type="button"
            onClick={() => onSelect(user)}
            className="flex w-full items-center gap-3 rounded-lg p-2 text-left transition-colors hover:bg-muted/60"
          >
            <UserAvatar user={user} />
            <div className="min-w-0 flex-1">
              <p className="text-sm font-medium truncate">{userTitle(user)}</p>
              <p className="text-xs text-muted-foreground truncate">{user.email}</p>
            </div>
          </button>
        </li>
      ))}
    </ul>
  );
}

function UserAvatar({ user }: { user: UserLookupResultDto }) {
  const initials = (user.displayName ?? user.username ?? user.email)
    .slice(0, 2)
    .toUpperCase();
  const src = user.avatarId ? `/api/files/${user.avatarId}/content` : undefined;
  return (
    <Avatar className="size-9 shrink-0">
      <AvatarImage src={src} alt={user.email} />
      <AvatarFallback>{initials}</AvatarFallback>
    </Avatar>
  );
}

function userTitle(user: UserLookupResultDto): string {
  if (user.displayName) return user.displayName;
  if (user.username) return user.username;
  return user.email;
}
