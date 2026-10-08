import { safeJsonLdStringify } from "./safe-stringify";

interface ProductJsonLdProps {
  name: string;
  description: string;
  brandName: string;
  price?: number | null;
  currency?: string;
  url: string;
  image?: string | null;
}

export function ProductJsonLd({
  name,
  description,
  brandName,
  price,
  currency = 'RUB',
  url,
  image,
}: ProductJsonLdProps) {
  const schema = {
    '@context': 'https://schema.org',
    '@type': 'Product',
    name,
    description,
    brand: {
      '@type': 'Brand',
      name: brandName,
    },
    ...(image && { image }),
    ...(price !== undefined && price !== null && {
      offers: {
        '@type': 'Offer',
        price: price.toString(),
        priceCurrency: currency,
        availability: 'https://schema.org/InStock',
        url,
      },
    }),
    url,
  };

  return (
    <script
      type="application/ld+json"
      dangerouslySetInnerHTML={{ __html: safeJsonLdStringify(schema) }}
    />
  );
}
