import { redirect } from "next/navigation";

/**
 * `/trainer/progress` (#568) — прогресс переехал во вкладку хаба. Старый роут
 * 307-редиректит на `/trainer?tab=progress`, чтобы прежние ссылки/закладки жили.
 */
export default function TrainerProgressRedirect() {
  redirect("/trainer?tab=progress");
}
