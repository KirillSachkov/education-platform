import { redirect } from "next/navigation";

import { routes } from "@/shared/config/routes";

/**
 * Управление тренажёром консолидировано в админку тренажёра `/trainer/admin`
 * (#623, в его собственном пространстве). Старый адрес `/author/trainer`
 * редиректит на вкладку «Контент» хаба, чтобы прежние ссылки продолжали работать.
 */
export default function AuthorTrainerPage() {
  redirect(`${routes.trainerAdmin}?tab=content`);
}
