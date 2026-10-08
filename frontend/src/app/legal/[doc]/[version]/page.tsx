import type { Metadata } from "next";
import Link from "next/link";
import { notFound } from "next/navigation";
import ReactMarkdown from "react-markdown";
import {
  CURRENT_LEGAL_VERSIONS,
  isLegalDocSlug,
  LEGAL_DOC_TITLES,
  loadLegalDocument,
} from "@/shared/legal";

// Private documents are mounted after the public image is built.
export const dynamic = "force-dynamic";

interface Props {
  params: Promise<{ doc: string; version: string }>;
}

const VERSION_PATTERN = /^v\d+$/;

export async function generateMetadata({ params }: Props): Promise<Metadata> {
  const { doc, version } = await params;
  if (!isLegalDocSlug(doc) || !VERSION_PATTERN.test(version)) {
    return { title: "Документ не найден" };
  }
  return {
    title: `${LEGAL_DOC_TITLES[doc]} (${version})`,
    robots: { index: false, follow: true },
  };
}

export default async function LegalDocVersionPage({ params }: Props) {
  const { doc, version } = await params;

  if (!isLegalDocSlug(doc) || !VERSION_PATTERN.test(version)) {
    notFound();
  }

  const content = await loadLegalDocument(doc, version);

  if (!content) {
    notFound();
  }

  const isCurrent = CURRENT_LEGAL_VERSIONS[doc] === version;

  return (
    <>
      {!isCurrent && (
        <div className="not-prose mb-6 rounded-lg border border-amber-500/40 bg-amber-500/10 p-4">
          <p className="text-sm">
            Это архивная версия документа. Текущая версия:{" "}
            <Link href={`/legal/${doc}`} className="font-semibold underline hover:text-foreground">
              {LEGAL_DOC_TITLES[doc]}
            </Link>
            .
          </p>
        </div>
      )}
      <ReactMarkdown>{content}</ReactMarkdown>
    </>
  );
}
