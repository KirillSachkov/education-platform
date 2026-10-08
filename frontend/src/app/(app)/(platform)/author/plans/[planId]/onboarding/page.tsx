import { redirect } from "next/navigation";
import { routes } from "@/shared/config/routes";

interface Props {
  params: Promise<{ planId: string }>;
}

export default async function PlanOnboardingPage({ params }: Props) {
  const { planId } = await params;
  redirect(routes.authorPlanDetail(planId));
}
