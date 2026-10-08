import type { Metadata } from "next";
import { ChatBindingsList } from "@/features/admin-plan-telegram";
import { AuthorPlanDetail } from "@/features/author-plans";
import { OnboardingFlowEditor } from "@/features/plan-onboarding-edit";
import { PlanHomePinsEditor } from "@/features/plan-home-pins-edit";

export const metadata: Metadata = {
  title: "План доступа",
};

interface Props {
  params: Promise<{ planId: string }>;
}

export default async function AuthorPlanDetailPage({ params }: Props) {
  const { planId } = await params;
  return (
    <AuthorPlanDetail
      planId={planId}
      onboardingEditor={<OnboardingFlowEditor planId={planId} />}
      chatBindings={<ChatBindingsList planId={planId} />}
      homePinsEditor={<PlanHomePinsEditor planId={planId} />}
    />
  );
}
