import type { Metadata } from "next";
import { AccessCommunitiesTab } from "@/features/admin-access-communities";
import { AdminUserDetailPage } from "@/features/admin-user-detail";

export const metadata: Metadata = {
  title: "Карточка пользователя",
};

export default async function AdminUserDetailRoute({
  params,
}: {
  params: Promise<{ userId: string }>;
}) {
  const { userId } = await params;
  return (
    <AdminUserDetailPage
      userId={userId}
      accessCommunitiesSlot={<AccessCommunitiesTab userId={userId} />}
    />
  );
}
