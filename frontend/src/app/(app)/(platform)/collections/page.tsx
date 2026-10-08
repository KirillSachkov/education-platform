import { redirect } from "next/navigation";
import { routes } from "@/shared/config/routes";

// Стандалонной страницы списка подборок больше нет — все подборки живут в базе знаний.
export default async function SpaceCollectionsPage() {
  redirect(routes.knowledgeBase);
}
