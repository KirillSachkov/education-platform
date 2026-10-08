import { InviteLandingView } from "@/features/redeem-invite";
import type { Metadata } from "next";

export const metadata: Metadata = {
  title: "Активация приглашения",
  robots: { index: false, follow: false },
};

interface InvitePageProps {
  params: Promise<{ token: string }>;
}

export default async function InvitePage({ params }: InvitePageProps) {
  const { token } = await params;
  return <InviteLandingView token={token} />;
}
