import type { Metadata } from "next";
import { CreatePlanForm } from "@/features/author-plans";

export const metadata: Metadata = {
  title: "Новый план доступа",
};

export default function CreatePlanPage() {
  return <CreatePlanForm />;
}
