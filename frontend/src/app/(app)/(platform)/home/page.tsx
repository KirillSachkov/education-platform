import type { Metadata } from "next";
import { HomeClient } from "./home-client";

export const metadata: Metadata = {
  title: "Моё обучение",
};

export default function HomePage() {
  return <HomeClient />;
}
