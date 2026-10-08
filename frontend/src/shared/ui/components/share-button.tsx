"use client";

import type { ComponentProps } from "react";
import { toast } from "sonner";
import { toAbsoluteUrl } from "@/shared/config/site";
import { cn } from "@/shared/lib/css";
import { Icons } from "@/shared/ui/icons";
import { Button } from "@/shared/ui/kit/button";

interface ShareButtonProps {
  /**
   * Path of the page to share, e.g. `/knowledge-base/<id>`. Resolved to an
   * absolute URL via `toAbsoluteUrl`. When omitted, the current page URL is
   * used (handy for pages whose canonical path isn't trivially available).
   */
  url?: string;
  /** Entity name — used as the native share-sheet title. */
  title: string;
  /** Optional descriptive text for the native share sheet. */
  text?: string;
  /**
   * Optional async resolver for a preferred share URL (e.g. a short link),
   * called lazily on click. Returns a path or absolute URL; `null`, or any
   * rejection, silently falls back to `url` / current location — sharing
   * must never break because of the resolver.
   */
  resolveUrl?: () => Promise<string | null>;
  /** Render the "Поделиться" text label next to the icon (hidden on mobile). */
  withLabel?: boolean;
  variant?: ComponentProps<typeof Button>["variant"];
  size?: ComponentProps<typeof Button>["size"];
  className?: string;
}

/**
 * Share button — native share sheet where available (mobile / PWA / some
 * desktops), otherwise copies the link to the clipboard with a toast.
 *
 * Capability is checked inside the click handler, never during render, so the
 * server and client produce identical markup (no hydration mismatch).
 */
export function ShareButton({
  url,
  title,
  text,
  resolveUrl,
  withLabel = true,
  variant = "outline",
  size = "sm",
  className,
}: ShareButtonProps) {
  async function handleShare() {
    let shareUrl =
      url !== undefined
        ? toAbsoluteUrl(url)
        : typeof window !== "undefined"
          ? window.location.href
          : "";

    // Preferred URL (short link) — resolved lazily on click so anonymous /
    // unconfigured callers never hit the API. Any failure keeps the fallback.
    if (resolveUrl) {
      try {
        const resolved = await resolveUrl();
        if (resolved) shareUrl = toAbsoluteUrl(resolved);
      } catch {
        // Short-link API unavailable (429/500/offline) — share the long URL.
      }
    }

    if (!shareUrl) return;

    const payload: ShareData = { title, url: shareUrl };
    if (text) payload.text = text;

    // navigator.share + canShare require a secure context — guaranteed on prod
    // (HTTPS) and treated as secure on localhost.
    if (typeof navigator !== "undefined" && typeof navigator.share === "function") {
      const canShare = navigator.canShare?.(payload) ?? true;
      if (canShare) {
        try {
          await navigator.share(payload);
          return;
        } catch (error) {
          // User dismissed the sheet — silent. Any other failure falls through
          // to the clipboard copy below.
          if (error instanceof DOMException && error.name === "AbortError") return;
        }
      }
    }

    try {
      await navigator.clipboard.writeText(shareUrl);
      toast.success("Ссылка скопирована");
    } catch {
      toast.error("Не удалось скопировать ссылку");
    }
  }

  return (
    <Button
      type="button"
      variant={variant}
      size={size}
      className={cn("gap-1.5", className)}
      onClick={handleShare}
      title="Поделиться"
      aria-label="Поделиться"
    >
      <Icons.shareIos className="size-3.5" />
      {withLabel && <span className="hidden sm:inline">Поделиться</span>}
    </Button>
  );
}
