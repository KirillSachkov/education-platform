"use client";

import { useMyProfile } from "@/features/profile-manage";

function firstNameOf(value: string | null | undefined): string | null {
  if (!value) return null;
  const trimmed = value.trim();
  if (!trimmed) return null;
  return trimmed.split(/\s+/)[0];
}

export function HomeWelcomeHeader() {
  const { profile } = useMyProfile();
  const firstName = firstNameOf(profile?.displayName) ?? firstNameOf(profile?.username);
  const greeting = firstName ? `Привет, ${firstName}!` : "Привет!";

  return (
    <header>
      <h1 className="text-2xl sm:text-3xl font-bold tracking-tight">{greeting}</h1>
    </header>
  );
}
