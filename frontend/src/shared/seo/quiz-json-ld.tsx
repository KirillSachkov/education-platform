import { safeJsonLdStringify } from "./safe-stringify";

interface QuizJsonLdProps {
  name: string;
  description: string;
  url: string;
  providerName: string;
  /** Тема теста для `about` + `educationalAlignment` (например, «.NET»). */
  about: string;
}

/**
 * Schema.org `Quiz` JSON-LD для лендинга теста уровня (#482).
 * `hasPart` (перечисление вопросов) намеренно НЕ эмитится — вопросы и есть продукт.
 * Output экранируется через `safeJsonLdStringify` (handles `</script>`, U+2028/9) —
 * тот же паттерн, что и остальной `shared/seo/`; inputs — статический copy страницы.
 */
export function QuizJsonLd({ name, description, url, providerName, about }: QuizJsonLdProps) {
  const schema = {
    "@context": "https://schema.org",
    "@type": "Quiz",
    name,
    description,
    url,
    about: {
      "@type": "Thing",
      name: about,
    },
    educationalAlignment: [
      {
        "@type": "AlignmentObject",
        alignmentType: "educationalSubject",
        targetName: about,
      },
    ],
    provider: {
      "@type": "Organization",
      name: providerName,
    },
  };

  return (
    <script
      type="application/ld+json"
      dangerouslySetInnerHTML={{ __html: safeJsonLdStringify(schema) }}
    />
  );
}
