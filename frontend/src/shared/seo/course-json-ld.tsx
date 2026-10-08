import { safeJsonLdStringify } from "./safe-stringify";

interface CourseJsonLdProps {
  name: string;
  description: string;
  providerName: string;
  /** Absolute URL of the provider (platform root) — strengthens the Organization link. */
  providerUrl?: string;
  instructorName: string;
  price?: number | null;
  currency?: string;
  url: string;
  /** Absolute cover image URL — Google Course rich-result favours an image. */
  image?: string | null;
  /** BCP-47 language tag of the course content. */
  inLanguage?: string;
}

export function CourseJsonLd({
  name,
  description,
  providerName,
  providerUrl,
  instructorName,
  price,
  currency = "RUB",
  url,
  image,
  inLanguage = "ru",
}: CourseJsonLdProps) {
  const schema = {
    "@context": "https://schema.org",
    "@type": "Course",
    name,
    description,
    inLanguage,
    ...(image && { image }),
    provider: {
      "@type": "Organization",
      name: providerName,
      ...(providerUrl && { url: providerUrl }),
    },
    instructor: {
      "@type": "Person",
      name: instructorName,
    },
    ...(price !== undefined &&
      price !== null && {
        offers: {
          "@type": "Offer",
          category: "Paid",
          price: price.toString(),
          priceCurrency: currency,
          availability: "https://schema.org/InStock",
          url,
        },
      }),
    // `courseMode: "online"` is the schema.org-conventional value (the previous
    // "OnlineCourse" is not a recognised mode). `courseWorkload` is omitted on
    // purpose: a fabricated `PT0H` (zero hours) is worse than absent — it told
    // crawlers the course takes no time.
    hasCourseInstance: {
      "@type": "CourseInstance",
      courseMode: "online",
    },
    url,
  };

  return (
    <script
      type="application/ld+json"
      dangerouslySetInnerHTML={{ __html: safeJsonLdStringify(schema) }}
    />
  );
}
