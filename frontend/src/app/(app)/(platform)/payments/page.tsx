import type { Metadata } from "next";
import { PaymentsView } from "@/widgets/payments-view";

export const metadata: Metadata = {
  title: "Платежи",
};

export default function PaymentsPage() {
  return <PaymentsView />;
}
