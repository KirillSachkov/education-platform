import { AppProviders } from "@/shared/providers/app-providers";

export default function InviteLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  // Wraps in AppProviders so React Query can fetch the public preview without
  // the (app) authenticated layout — invite landing must work for anonymous users
  // who haven't logged in yet.
  return <AppProviders>{children}</AppProviders>;
}
