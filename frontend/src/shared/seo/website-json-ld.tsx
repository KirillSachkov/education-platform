import { safeJsonLdStringify } from "./safe-stringify";

interface WebSiteJsonLdProps {
  name: string;
  url: string;
  description?: string;
  inLanguage?: string;
  /** Name of the publishing organization (links the WebSite to the Organization entity). */
  publisherName?: string;
}

/**
 * Schema.org `WebSite` JSON-LD for the platform root page.
 *
 * No `potentialAction` / `SearchAction` (sitelinks searchbox) is emitted on
 * purpose: the platform has no public `/search?q=` results page (global search
 * lives behind auth), and Google requires the search URL template to actually
 * return results — advertising a broken one is worse than omitting it.
 *
 * Output is escaped via `safeJsonLdStringify` (handles `</script>`, U+2028/9).
 * Inputs are static config, no user-supplied data reaches this component.
 */
export function WebSiteJsonLd({
  name,
  url,
  description,
  inLanguage = "ru",
  publisherName,
}: WebSiteJsonLdProps) {
  const schema = {
    "@context": "https://schema.org",
    "@type": "WebSite",
    name,
    url,
    inLanguage,
    ...(description && { description }),
    ...(publisherName && {
      publisher: {
        "@type": "Organization",
        name: publisherName,
      },
    }),
  };

  return (
    <script
      type="application/ld+json"
      dangerouslySetInnerHTML={{ __html: safeJsonLdStringify(schema) }}
    />
  );
}
