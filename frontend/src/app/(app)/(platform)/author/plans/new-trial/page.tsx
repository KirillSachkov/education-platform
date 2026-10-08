import type { Metadata } from "next";
import { CreateTrialPlanForm } from "@/features/author-plans";

export const metadata: Metadata = {
  title: "Пробный доступ",
};

export default function CreateTrialPlanPage() {
  return <CreateTrialPlanForm />;
}
