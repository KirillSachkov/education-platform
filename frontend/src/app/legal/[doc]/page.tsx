import type { Metadata } from "next";
import { notFound } from "next/navigation";
import ReactMarkdown from "react-markdown";
import {
  CURRENT_LEGAL_VERSIONS,
  isLegalDocSlug,
  LEGAL_DOC_DESCRIPTIONS,
  LEGAL_DOC_TITLES,
  legalDocPdfHref,
  loadLegalDocument,
} from "@/shared/legal";
import { Icons } from "@/shared/ui/icons";
import { loadBusinessDetails } from "@/shared/business-details/server";

// Private documents are mounted after the public image is built.
export const dynamic = "force-dynamic";

interface Props {
  params: Promise<{ doc: string }>;
}

export async function generateMetadata({ params }: Props): Promise<Metadata> {
  const { doc } = await params;
  if (!isLegalDocSlug(doc)) {
    return { title: "Документ не найден" };
  }
  return {
    title: LEGAL_DOC_TITLES[doc],
    description: LEGAL_DOC_DESCRIPTIONS[doc],
  };
}

export default async function LegalDocPage({ params }: Props) {
  const { doc } = await params;

  if (!isLegalDocSlug(doc)) {
    notFound();
  }

  const version = CURRENT_LEGAL_VERSIONS[doc];
  const content = await loadLegalDocument(doc, version);

  if (!content) {
    notFound();
  }

  const businessDetails = await loadBusinessDetails();

  return (
    <>
      <div className="not-prose mb-6 flex justify-end">
        <a
          href={legalDocPdfHref(doc)}
          download
          className="inline-flex items-center gap-2 rounded-lg border border-border/60 px-3 py-2 text-sm font-medium hover:border-primary/40 hover:text-primary transition-colors"
        >
          <Icons.download className="h-4 w-4" />
          Скачать PDF
        </a>
      </div>
      <ReactMarkdown>{content}</ReactMarkdown>
      <hr className="my-8" />
      <p className="text-sm text-muted-foreground not-prose">
        Версия документа: <code className="px-1 py-0.5 rounded bg-muted text-xs">{version}</code>.
        {businessDetails ? (
          <>
            {" "}
            Архив всех версий доступен по запросу на{" "}
            <a href={`mailto:${businessDetails.email}`} className="underline hover:text-foreground">
              {businessDetails.email}
            </a>
            .
          </>
        ) : null}
      </p>
    </>
  );
}
