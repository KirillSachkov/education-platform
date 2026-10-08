// The re-run mutation is a pure AI-review domain operation, so the
// implementation now lives at the entity layer (`@/entities/ai-review`),
// where it can be consumed by any feature/widget without crossing an FSD
// feature boundary. This re-export keeps the historical feature public API
// (`@/features/run-ai-iteration`) intact for existing callers.
export { useRunAiIteration } from "@/entities/ai-review";
