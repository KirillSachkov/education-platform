import type { Metadata } from "next";
import { AdminAuditLogPage } from "@/features/admin-audit-log";

export const metadata: Metadata = {
  title: "Лог админ-действий",
};

export default function AdminAuditLogRoute() {
  return <AdminAuditLogPage />;
}
