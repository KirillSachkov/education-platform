/** Content model for a keyword SEO landing page (`/c-sharp`, `/dotnet`, `/asp-net-core`). */

export interface SeoLandingCta {
  label: string;
  href: string;
}

export interface SeoLandingSection {
  /** Optional anchor id. */
  id?: string;
  heading: string;
  /** Paragraphs of unique body copy. */
  body: string[];
  /** Optional bullet list rendered under the paragraphs. */
  bullets?: string[];
}

export interface SeoLandingFaqItem {
  question: string;
  answer: string;
}

export interface SeoLandingRelated {
  title: string;
  href: string;
}

export interface SeoLandingContent {
  /** Path of this page, e.g. "/c-sharp" — used for canonical + breadcrumb. */
  slug: string;

  // --- <head> ---
  metaTitle: string;
  metaDescription: string;

  // --- Hero ---
  /** Small label above the H1, e.g. "Курс C#". */
  eyebrow: string;
  h1: string;
  heroLead: string;
  heroCtaPrimary: SeoLandingCta;
  heroCtaSecondary?: SeoLandingCta;

  // --- Body ---
  sections: SeoLandingSection[];
  faq: SeoLandingFaqItem[];

  // --- Final CTA ---
  finalCtaHeading: string;
  finalCtaSub: string;
  finalCtaPrimary: SeoLandingCta;
  finalCtaSecondary?: SeoLandingCta;

  /** Cross-links to the sibling SEO landings — topical cluster + crawl paths. */
  related: SeoLandingRelated[];

  /** Breadcrumb label for this page. */
  breadcrumbName: string;
}
