import { safeJsonLdStringify } from "./safe-stringify";

interface ArticleJsonLdProps {
  headline: string;
  description?: string | null;
  image?: string | null;
  datePublished?: string | null;
  dateModified?: string | null;
  /** Имя автора-человека; без него автором становится организация-издатель. */
  authorName?: string | null;
  publisherName: string;
  url: string;
}

/**
 * Schema.org `Article` JSON-LD for material detail pages (knowledge base + course learn).
 * Output is escaped via `safeJsonLdStringify` (handles `</script>`, U+2028/9) — same
 * pattern as the rest of `shared/seo/`. Author/publisher mirror the Organization/Person
 * split used by `course-json-ld.tsx` / `organization-json-ld.tsx`.
 */
export function ArticleJsonLd({
  headline,
  description,
  image,
  datePublished,
  dateModified,
  authorName,
  publisherName,
  url,
}: ArticleJsonLdProps) {
  const schema = {
    "@context": "https://schema.org",
    "@type": "Article",
    headline,
    ...(description && { description }),
    ...(image && { image }),
    ...(datePublished && { datePublished }),
    ...(dateModified && { dateModified }),
    author: authorName
      ? { "@type": "Person", name: authorName }
      : { "@type": "Organization", name: publisherName },
    publisher: { "@type": "Organization", name: publisherName },
    mainEntityOfPage: { "@type": "WebPage", "@id": url },
    url,
  };

  return (
    <script
      type="application/ld+json"
      dangerouslySetInnerHTML={{ __html: safeJsonLdStringify(schema) }}
    />
  );
}
