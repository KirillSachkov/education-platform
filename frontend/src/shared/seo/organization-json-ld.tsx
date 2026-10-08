import { safeJsonLdStringify } from "./safe-stringify";

interface OrganizationJsonLdProps {
  name: string;
  url: string;
  logo?: string;
  email?: string;
  description?: string;
  sameAs?: string[];
}

/**
 * Schema.org `Organization` JSON-LD for the platform root page.
 * Output is escaped via `safeJsonLdStringify` (handles `</script>`, U+2028/9) — same
 * pattern as the rest of `shared/seo/`. Inputs are static config (org name/url), no
 * user-supplied data ever reaches this component.
 */
export function OrganizationJsonLd({
  name,
  url,
  logo,
  email,
  description,
  sameAs,
}: OrganizationJsonLdProps) {
  const schema = {
    "@context": "https://schema.org",
    "@type": "Organization",
    name,
    url,
    ...(logo && { logo }),
    ...(description && { description }),
    ...(email && {
      contactPoint: {
        "@type": "ContactPoint",
        contactType: "customer support",
        email,
      },
    }),
    ...(sameAs && sameAs.length > 0 && { sameAs }),
  };

  return (
    <script
      type="application/ld+json"
      dangerouslySetInnerHTML={{ __html: safeJsonLdStringify(schema) }}
    />
  );
}
