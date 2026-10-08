import type { Metadata } from "next";
import { routes } from "@/shared/config/routes";
import { redirect } from "next/navigation";

export const metadata: Metadata = {
  title: "Мок-собесы",
};

export default function AuthorMockInterviewsPage() {
  redirect(`${routes.trainerAdmin}?tab=mock`);
}
