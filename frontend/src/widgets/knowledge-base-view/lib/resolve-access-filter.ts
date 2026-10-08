/**
 * The "only free" control promises value without registration, so it must map
 * to SearchService's strict anonymous PUBLIC tier rather than legacy `free`
 * (PUBLIC + AUTHENTICATED).
 */
export function resolveKnowledgeBaseAccessFilter(freeOnly: boolean): "public" | undefined {
  return freeOnly ? "public" : undefined;
}
