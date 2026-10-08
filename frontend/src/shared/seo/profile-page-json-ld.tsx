import { safeJsonLdStringify } from "./safe-stringify";

interface ProfilePageJsonLdProps {
  name: string;
  jobTitle?: string | null;
  url: string;
  description?: string | null;
}

export function ProfilePageJsonLd({
  name,
  jobTitle,
  url,
  description,
}: ProfilePageJsonLdProps) {
  const schema = {
    '@context': 'https://schema.org',
    '@type': 'ProfilePage',
    mainEntity: {
      '@type': 'Person',
      name,
      ...(jobTitle && { jobTitle }),
      ...(description && { description }),
      url,
    },
  };

  return (
    <script
      type="application/ld+json"
      dangerouslySetInnerHTML={{ __html: safeJsonLdStringify(schema) }}
    />
  );
}
