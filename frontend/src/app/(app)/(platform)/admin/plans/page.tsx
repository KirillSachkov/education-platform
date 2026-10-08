import type { Metadata } from "next";
import { AuthorPlansList } from "@/features/author-plans";

export const metadata: Metadata = {
  title: "Планы доступа",
};

export default function AdminPlansPage() {
  return <AuthorPlansList />;
}
