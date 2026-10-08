"use client";

import { Button } from "@/shared/ui/kit/button";
import { Lock, Unlock, X } from "lucide-react";

type BulkActionToolbarProps = {
  count: number;
  onClear: () => void;
  onBulkLock: () => void;
  onBulkUnlock: () => void;
  isPending: boolean;
};

export function BulkActionToolbar({
  count,
  onClear,
  onBulkLock,
  onBulkUnlock,
  isPending,
}: BulkActionToolbarProps) {
  if (count === 0) return null;

  return (
    <div className="mb-3 flex flex-col gap-2 rounded-md border bg-muted/40 px-3 py-2 sm:flex-row sm:items-center">
      <span className="text-sm font-medium">Выбрано: {count}</span>
      <div className="hidden flex-1 sm:block" />
      <div className="flex flex-wrap items-center gap-2">
        <Button
          size="sm"
          variant="outline"
          className="min-touch flex-1 sm:flex-none"
          onClick={onBulkLock}
          disabled={isPending}
        >
          <Lock className="mr-1.5 h-3.5 w-3.5" />
          Залочить
        </Button>
        <Button
          size="sm"
          variant="outline"
          className="min-touch flex-1 sm:flex-none"
          onClick={onBulkUnlock}
          disabled={isPending}
        >
          <Unlock className="mr-1.5 h-3.5 w-3.5" />
          Разлочить
        </Button>
        <Button
          size="sm"
          variant="ghost"
          className="min-touch"
          onClick={onClear}
          disabled={isPending}
          aria-label="Сбросить выбор"
        >
          <X className="h-3.5 w-3.5" />
        </Button>
      </div>
    </div>
  );
}
