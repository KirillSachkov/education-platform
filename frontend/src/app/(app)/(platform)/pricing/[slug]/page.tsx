import { redirect } from "next/navigation";
import { routes } from "@/shared/config/routes";

export default function RetiredPricingPage() {
  redirect(routes.home);
}
