import { AppLayout } from "@/widgets/layouts/app-layout";
import { readSidebarDefaultOpen } from "@/shared/lib/sidebar-server";

export default async function PlatformLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  const defaultSidebarOpen = await readSidebarDefaultOpen();
  return <AppLayout defaultSidebarOpen={defaultSidebarOpen}>{children}</AppLayout>;
}
