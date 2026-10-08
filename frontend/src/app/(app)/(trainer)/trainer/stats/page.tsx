import { redirect } from "next/navigation";

import { routes } from "@/shared/config/routes";

/**
 * `/trainer/stats` (#568) — статистика теперь живёт ВКЛАДКОЙ внутри хаба, не
 * отдельной страницей (решение владельца). Старый/закладочный URL 308-редиректим
 * на вкладку «Статистика» хаба, чтобы ссылки не били 404.
 */
export default function TrainerStatsPage() {
  redirect(routes.trainerProgress);
}
