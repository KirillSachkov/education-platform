"use client";

import { Button } from "@/shared/ui/kit/button";
import { cn } from "@/shared/lib/css";
import { ThumbsDown, ThumbsUp } from "lucide-react";
import { useState } from "react";
import { useSubmitIterationFeedback } from "./use-submit-iteration-feedback";

interface Props {
  iterationId: string;
}

/**
 * Issue #327 — 👍/👎 кнопки под iteration card. Optimistic state: после клика
 * выбранная кнопка подсвечивается, повторный клик/смена решения отправит
 * новый submit (backend upsert). Это сигнал для команды «хорошо ли AI ревьюит».
 */
export function IterationFeedbackButtons({ iterationId }: Props) {
  const mutation = useSubmitIterationFeedback();
  const [selected, setSelected] = useState<"up" | "down" | null>(null);

  const handleClick = (value: "up" | "down") => {
    setSelected(value);
    mutation.mutate({
      iterationId,
      isHelpful: value === "up",
    });
  };

  return (
    <div className="flex items-center gap-1">
      <span className="text-xs text-muted-foreground mr-1">Полезно?</span>
      <Button
        type="button"
        variant="ghost"
        size="icon"
        className={cn(
          "size-7",
          selected === "up" && "bg-green/10 text-green hover:bg-green/15",
        )}
        onClick={() => handleClick("up")}
        disabled={mutation.isPending}
        aria-label="Полезно"
      >
        <ThumbsUp className="size-3.5" />
      </Button>
      <Button
        type="button"
        variant="ghost"
        size="icon"
        className={cn(
          "size-7",
          selected === "down" && "bg-red/10 text-red hover:bg-red/15",
        )}
        onClick={() => handleClick("down")}
        disabled={mutation.isPending}
        aria-label="Не полезно"
      >
        <ThumbsDown className="size-3.5" />
      </Button>
    </div>
  );
}
