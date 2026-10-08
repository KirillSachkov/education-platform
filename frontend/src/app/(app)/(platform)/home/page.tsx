import type { Metadata } from "next";
import { HomeClient } from "./home-client";

export const metadata: Metadata = {
  title: "Главная",
};

export default function HomePage() {
  return <HomeClient />;
}
