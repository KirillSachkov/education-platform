import type { Metadata } from "next";
import { PlatformLeaderboardClient } from "./page-client";

export const metadata: Metadata = {
  title: "Рейтинг платформы",
  description:
    "Глобальный рейтинг учеников всей платформы — суммарный XP по всем авторам и курсам.",
};

export default function PlatformLeaderboardPage() {
  return <PlatformLeaderboardClient />;
}
