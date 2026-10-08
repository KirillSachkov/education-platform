import type { Metadata } from "next";
import { AdminAiUsagePage } from "@/features/admin-ai-usage";

export const metadata: Metadata = {
  title: "AI usage",
};

export default function AdminAiUsageRoute() {
  return <AdminAiUsagePage />;
}
