import type { Metadata } from "next";
import { KnowledgeBaseView } from "@/widgets/knowledge-base-view";

export const metadata: Metadata = {
  title: "База знаний",
  description: "Открытые материалы платформы: статьи, видео, заметки, стримы.",
};

// `KnowledgeBaseView` reads `useSearchParams()` at the top — Next.js refuses
// to prerender pages that depend on the runtime search-params bag unless the
// segment opts out of static generation. The space-scoped twin at
// `/@<slug>/knowledge-base` is already dynamic thanks to its `[authorSlug]`
// param; this canonical route has no dynamic segment so we declare it.
export const dynamic = "force-dynamic";

/**
 * Canonical knowledge-base for the single-tenant platform. Uses the same
 * `KnowledgeBaseView` widget as `/@<author>/knowledge-base` so search,
 * filters, free-only toggle, and collections preview are at parity — the old
 * stripped-down `MaterialsPage` rendering this route is gone.
 */
export default function MaterialsRoutePage() {
  return <KnowledgeBaseView />;
}
