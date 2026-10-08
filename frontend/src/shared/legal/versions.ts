/**
 * Текущие версии юридических документов на Платформе.
 * Sync c backend `LegalDocumentVersions` (AuthService.Core.Services).
 *
 * При обновлении документа:
 *   1. Bump версию в этом файле.
 *   2. Bump в backend LegalDocumentVersions.cs.
 *   3. Сохранить новый Markdown `{slug}-v{N+1}.md` в приватном LEGAL_DOCUMENTS_DIR.
 *   4. Старая версия остаётся для архива на `/legal/{slug}/v{N}`.
 *   5. Перегенерировать PDF в приватном LEGAL_PDFS_DIR; см. docs/legal.md.
 */
export const CURRENT_LEGAL_VERSIONS = {
  offer: "v2",
  privacy: "v3",
  "consent-pd": "v2",
  cookies: "v1",
  "consent-marketing": "v1",
} as const satisfies Record<string, string>;

export type LegalDocSlug = keyof typeof CURRENT_LEGAL_VERSIONS;

export const LEGAL_DOC_TITLES: Record<LegalDocSlug, string> = {
  offer: "Договор-оферта",
  privacy: "Политика обработки персональных данных",
  "consent-pd": "Согласие на обработку персональных данных",
  cookies: "Политика cookies",
  "consent-marketing": "Согласие на рекламные рассылки",
};

export const LEGAL_DOC_DESCRIPTIONS: Record<LegalDocSlug, string> = {
  offer: "Условия оказания информационно-консультационных услуг на Платформе",
  privacy: "Состав, цели и способы обработки персональных данных пользователей",
  "consent-pd": "Согласие пользователя на обработку персональных данных по 152-ФЗ",
  cookies: "Какие cookies используем и как ими управлять",
  "consent-marketing": "Согласие на получение рекламных и информационных рассылок",
};

export function isLegalDocSlug(value: string): value is LegalDocSlug {
  return Object.hasOwn(CURRENT_LEGAL_VERSIONS, value);
}

/**
 * Путь к PDF-версии документа (генерируется `scripts/generate-legal-pdfs.sh`
 * в `public/legal-docs/`). Версия по умолчанию — текущая.
 * Лежит под `/legal-docs/`, а не `/legal/`, чтобы не конфликтовать с роутом `/legal/[doc]`.
 */
export function legalDocPdfHref(
  slug: LegalDocSlug,
  version: string = CURRENT_LEGAL_VERSIONS[slug],
): string {
  return `/legal-docs/${slug}-${version}.pdf`;
}
