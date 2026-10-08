import type { Metadata } from "next";
import { AuthorPlansList } from "@/features/author-plans";

export const metadata: Metadata = {
  title: "Планы доступа",
};

export default function AuthorPlansPage() {
  return <AuthorPlansList />;
}
