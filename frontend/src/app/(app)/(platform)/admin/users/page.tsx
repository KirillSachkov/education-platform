import type { Metadata } from "next";
import { AdminUsersPage } from "@/features/admin-users";

export const metadata: Metadata = {
  title: "Управление пользователями",
};

export default function AdminUsersRoute() {
  return <AdminUsersPage />;
}
