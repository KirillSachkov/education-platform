import type { Metadata } from "next";
import { Suspense } from "react";
import { NotificationsCenter } from "@/widgets/notifications-center";

export const metadata: Metadata = {
  title: "Уведомления",
};

export default function NotificationsRoute() {
  return (
    <Suspense fallback={null}>
      <NotificationsCenter />
    </Suspense>
  );
}
