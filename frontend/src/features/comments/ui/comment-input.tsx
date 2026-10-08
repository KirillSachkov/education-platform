import { cn } from "@/shared/lib/css";
import { UserAvatar } from "@/shared/ui/components";
import { Button } from "@/shared/ui/kit/button";
import { Textarea } from "@/shared/ui/kit/textarea";
import { Loader2, Send } from "lucide-react";
import type React from "react";
import { useState } from "react";

export interface CommentInputProps extends React.ComponentProps<"textarea"> {
  onCancel?: () => void;
  isPending?: boolean;
  submitLabel?: string;
  error?: string;
  userName?: string | null;
  userAvatarId?: string | null;
  showAvatar?: boolean;
}

export const CommentInput = ({
  onCancel,
  isPending,
  submitLabel = "Отправить",
  error,
  userName,
  userAvatarId,
  showAvatar = true,
  placeholder = "Оставьте комментарий...",
  disabled,
  className,
  ...props
}: CommentInputProps) => {
  const [isFocused, setIsFocused] = useState(false);

  const handleCancel = () => {
    setIsFocused(false);
    onCancel?.();
  };

  return (
    <div className={cn("flex gap-2.5 sm:gap-3", className)}>
      {showAvatar && (
        <UserAvatar name={userName} avatarId={userAvatarId} className="size-8 shrink-0 sm:size-9" />
      )}

      <div className="flex flex-1 flex-col gap-2">
        <div
          className={cn(
            "rounded-2xl border bg-card/60 transition-all",
            isFocused
              ? "border-primary/40 ring-1 ring-primary/20"
              : "border-border/60 hover:border-border",
          )}
        >
          <Textarea
            placeholder={placeholder}
            disabled={disabled || isPending}
            rows={isFocused ? 3 : 1}
            onFocus={() => setIsFocused(true)}
            className={cn(
              "resize-none border-0 bg-transparent shadow-none rounded-2xl",
              "px-4 py-3",
              "placeholder:text-muted-foreground/50",
              "focus-visible:ring-0",
            )}
            {...props}
          />
        </div>

        {error && <p className="px-1 text-xs text-destructive">{error}</p>}

        {isFocused && (
          <div className="flex justify-end gap-2">
            <Button
              type="button"
              variant="ghost"
              size="sm"
              className="rounded-full text-muted-foreground"
              onClick={handleCancel}
              disabled={isPending}
            >
              Отмена
            </Button>
            <Button
              type="submit"
              size="sm"
              className="rounded-full gap-1.5"
              disabled={disabled || isPending}
            >
              {isPending ? (
                <Loader2 className="size-3.5 animate-spin" />
              ) : (
                <Send className="size-3.5" />
              )}
              {submitLabel}
            </Button>
          </div>
        )}
      </div>
    </div>
  );
};
