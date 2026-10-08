"use client";

import { Button } from "@/shared/ui/kit/button";
import { RefreshCw } from "lucide-react";

export function OfflineRetryButton() {
  return (
    <Button
      variant="default"
      className="gap-2 rounded-xl"
      onClick={() => {
        if (typeof window !== "undefined") window.location.reload();
      }}
    >
      <RefreshCw className="size-4" />
      Повторить попытку
    </Button>
  );
}
