import type { Metadata } from "next";
import { AdminCampaignsPage } from "@/features/admin-campaigns";

export const metadata: Metadata = {
  title: "Рассылки",
};

export default function AdminCampaignsRoute() {
  return <AdminCampaignsPage />;
}
