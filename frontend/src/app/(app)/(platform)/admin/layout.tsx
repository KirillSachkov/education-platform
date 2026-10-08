"use client";

import { ROLES, RequireRole } from "@/shared/auth";

export default function AdminLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  return <RequireRole atLeast={ROLES.ADMIN}>{children}</RequireRole>;
}
