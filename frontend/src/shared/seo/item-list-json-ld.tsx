import { safeJsonLdStringify } from "./safe-stringify";

interface ItemListJsonLdProps {
  /** Ordered list entries — each becomes a `ListItem` with absolute `url`. */
  items: { name: string; url: string }[];
}

/**
 * Schema.org `ItemList` JSON-LD for a catalog / index page (e.g. `/courses`).
 *
 * Helps crawlers understand the page is a curated list and surfaces the member
 * URLs as a group. Output is escaped via `safeJsonLdStringify`; course titles
 * are author-controlled (not anonymous-user input) but still pass through the
 * same `</script>` / U+2028 hardening as the rest of `shared/seo/`.
 */
export function ItemListJsonLd({ items }: ItemListJsonLdProps) {
  const schema = {
    "@context": "https://schema.org",
    "@type": "ItemList",
    itemListElement: items.map((item, index) => ({
      "@type": "ListItem",
      position: index + 1,
      name: item.name,
      url: item.url,
    })),
  };

  return (
    <script
      type="application/ld+json"
      dangerouslySetInnerHTML={{ __html: safeJsonLdStringify(schema) }}
    />
  );
}
