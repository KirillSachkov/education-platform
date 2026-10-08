import type { Metadata } from "next";
import { MyPlansView } from "@/widgets/my-plans-view";

export const metadata: Metadata = {
  title: "Мои планы",
};

export default function MyPlansPage() {
  return <MyPlansView />;
}
