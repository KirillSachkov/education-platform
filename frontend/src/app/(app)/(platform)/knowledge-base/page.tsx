import { redirect } from "next/navigation";
import { routes } from "@/shared/config/routes";

export default function KnowledgeBasePage() {
  redirect(routes.home);
}
