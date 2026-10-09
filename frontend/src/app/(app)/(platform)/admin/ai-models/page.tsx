import type { Metadata } from "next";
import { AssignmentReviewAiSection } from "@/features/admin-assignment-review-ai";

export const metadata: Metadata = {
  title: "AI модели",
};

export default function AdminAiModelsRoute() {
  return (
    <div className="space-y-8">
      <section className="space-y-2">
        <header>
          <h2 className="text-2xl font-semibold tracking-tight">AI-модели для проверки заданий</h2>
          <p className="max-w-3xl text-sm text-muted-foreground">
            Настройки AssignmentReviewService — какая модель ревьюит student PR&rsquo;ы, embedder и
            cheap triage. Reviewer применяется на следующий{" "}
            <code className="rounded bg-muted px-1 py-0.5 text-xs">run-iteration</code>.
          </p>
        </header>
        <AssignmentReviewAiSection />
      </section>
    </div>
  );
}
