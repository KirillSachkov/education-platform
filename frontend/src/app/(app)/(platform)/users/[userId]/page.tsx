import type { Metadata } from "next";
import { UserProfileClient } from "./page-client";

const API_URL =
  process.env.API_URL_INTERNAL ?? process.env.NEXT_PUBLIC_API_URL ?? "http://localhost/api";

interface Props {
  params: Promise<{ userId: string }>;
}

export async function generateMetadata({ params }: Props): Promise<Metadata> {
  const { userId } = await params;
  try {
    const res = await fetch(`${API_URL}/users/${userId}/public-profile/`, {
      cache: "no-store",
    });
    if (!res.ok) return { title: "Профиль пользователя" };
    const data = await res.json();
    const profile = data.result;
    const name = profile?.displayName ?? profile?.username ?? "Профиль";
    return {
      title: name,
      description: profile?.bio ?? profile?.aboutAsAuthor ?? undefined,
    };
  } catch {
    return { title: "Профиль пользователя" };
  }
}

export default async function UserProfilePage({ params }: Props) {
  const { userId } = await params;
  return <UserProfileClient userId={userId} />;
}
