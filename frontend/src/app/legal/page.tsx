import Link from "next/link";
import {
  CURRENT_LEGAL_VERSIONS,
  LEGAL_DOC_TITLES,
  LEGAL_DOC_DESCRIPTIONS,
  legalDocPdfHref,
  type LegalDocSlug,
} from "@/shared/legal";
import { Icons } from "@/shared/ui/icons";

const SLUGS = Object.keys(CURRENT_LEGAL_VERSIONS) as LegalDocSlug[];

export default function LegalIndexPage() {
  return (
    <>
      <h1>Юридические документы</h1>
      <p>
        Документы, регламентирующие использование Платформы и обработку персональных данных
        пользователей.
      </p>

      <ul className="not-prose space-y-3 mt-6">
        {SLUGS.map((slug) => (
          <li
            key={slug}
            className="border border-border/60 rounded-lg p-4 hover:border-primary/40 transition-colors"
          >
            <Link href={`/legal/${slug}`} className="block group">
              <div className="text-base font-medium text-foreground group-hover:text-primary transition-colors">
                {LEGAL_DOC_TITLES[slug]}
              </div>
              <div className="text-sm text-muted-foreground mt-1">
                {LEGAL_DOC_DESCRIPTIONS[slug]}
              </div>
            </Link>
            <div className="flex items-center justify-between gap-3 mt-3">
              <span className="text-xs text-muted-foreground/70">
                Версия {CURRENT_LEGAL_VERSIONS[slug]}
              </span>
              <a
                href={legalDocPdfHref(slug)}
                download
                className="inline-flex items-center gap-1.5 text-xs font-medium text-muted-foreground hover:text-primary transition-colors"
              >
                <Icons.download className="h-3.5 w-3.5" />
                Скачать PDF
              </a>
            </div>
          </li>
        ))}
      </ul>
    </>
  );
}
