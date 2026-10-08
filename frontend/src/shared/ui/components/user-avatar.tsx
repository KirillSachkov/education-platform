"use client";

import { API_ORIGIN } from "@/shared/api";
import { buildContentImageSrcSet } from "@/shared/lib/image-src";
import { routes } from "@/shared/config/routes";
import { Avatar, AvatarFallback, AvatarImage } from "@/shared/ui/kit/avatar";
import { cn } from "@/shared/lib/css";
import Link from "next/link";

function getInitials(name?: string | null): string {
  if (!name) return "?";
  return name
    .split(" ")
    .map((w) => w[0])
    .join("")
    .toUpperCase()
    .slice(0, 2);
}

function getAvatarUrl(avatarId: string | null | undefined): string | undefined {
  if (!avatarId) return undefined;
  return `${API_ORIGIN}/files/${avatarId}/content`;
}

interface UserAvatarProps {
  name?: string | null | undefined;
  avatarId?: string | null | undefined;
  className?: string;
  /** When provided, the avatar becomes a link to the user's public profile */
  userId?: string | null;
}

export function UserAvatar({ name, avatarId, className, userId }: UserAvatarProps) {
  const url = getAvatarUrl(avatarId);
  const srcSet = buildContentImageSrcSet(url);
  const initials = getInitials(name);

  const avatar = (
    <Avatar className={cn("size-8", className)}>
      {url && <AvatarImage src={url} srcSet={srcSet} sizes="96px" alt={name ?? "Avatar"} />}
      <AvatarFallback className="bg-gradient-primary text-2xs font-bold text-primary-foreground">
        {initials}
      </AvatarFallback>
    </Avatar>
  );

  if (userId) {
    return (
      <Link
        href={routes.userProfile(userId)}
        onClick={(e) => e.stopPropagation()}
        className="shrink-0 rounded-full ring-0 hover:ring-2 hover:ring-primary/40 transition-shadow"
      >
        {avatar}
      </Link>
    );
  }

  return avatar;
}
