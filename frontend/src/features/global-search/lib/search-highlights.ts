import type { SearchEducationDocumentDto, SearchHighlight } from "@/entities/search";

// chapter_titles намеренно НЕ в priority — он рендерится отдельным chip'ом
// через pickChapterMatch (с таймкодом и deep-link href'ом), а не как обычный
// body-snippet.
const HIGHLIGHT_PRIORITY = ["title", "description"] as const;

/**
 * Совпадение по конкретной главе видео — backend кладёт matched chapter index в
 * `highlight.matchedIndices`, а параллельные массивы `chapterTitles` /
 * `chapterTimestamps` лежат на самом документе. Возвращаем первое совпадение,
 * чтобы UI показал «По главе MM:SS» + сниппет с подсветкой.
 */
export interface ChapterMatch {
  /** HTML с <mark> вокруг совпадения. */
  snippet: string;
  /** Заголовок главы без подсветки — для chip / aria-label. */
  title: string;
  /** Offset в секундах для deep-link `?t=<seconds>`. */
  timestampSeconds: number;
}

export function pickChapterMatch(
  highlights: SearchHighlight[],
  document: Pick<SearchEducationDocumentDto, "chapterTitles" | "chapterTimestamps">,
): ChapterMatch | null {
  const chapterHighlight = highlights.find(
    (highlight) =>
      highlight.field === "chapter_titles" && normalizeSnippet(highlight.snippet).length > 0,
  );
  if (!chapterHighlight) return null;

  const matchedIndex = chapterHighlight.matchedIndices?.[0];
  if (matchedIndex === undefined) return null;

  const titles = document.chapterTitles ?? [];
  const timestamps = document.chapterTimestamps ?? [];
  const title = titles[matchedIndex];
  const timestamp = timestamps[matchedIndex];
  if (title === undefined || timestamp === undefined) return null;

  return {
    snippet: normalizeSnippet(chapterHighlight.snippet),
    title,
    timestampSeconds: Math.max(0, Math.floor(timestamp)),
  };
}

export function formatChapterTimestamp(seconds: number): string {
  const total = Math.max(0, Math.floor(seconds));
  const h = Math.floor(total / 3600);
  const m = Math.floor((total % 3600) / 60);
  const s = total % 60;
  const pad = (n: number) => n.toString().padStart(2, "0");
  return h > 0 ? `${h}:${pad(m)}:${pad(s)}` : `${m}:${pad(s)}`;
}

function normalizeSnippet(snippet: string) {
  return snippet
    .replace(/\s+/g, " ")
    .replace(/^\u2026+|\u2026+$/g, "")
    .trim();
}

function escapeHtml(value: string) {
  return value
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#39;");
}

function escapeRegex(value: string) {
  return value.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}

export function pickSearchTitleHighlight(highlights: SearchHighlight[]) {
  const titleHighlight = highlights.find(
    (highlight) => highlight.field === "title" && normalizeSnippet(highlight.snippet).length > 0,
  );

  return titleHighlight ? normalizeSnippet(titleHighlight.snippet) : null;
}

export function buildSearchTitleFallbackHighlight(title: string, query: string) {
  const tokens = query
    .trim()
    .split(/\s+/)
    .map((token) => token.trim())
    .filter(Boolean);

  if (tokens.length === 0) {
    return null;
  }

  const pattern = tokens
    .sort((left, right) => right.length - left.length)
    .map(escapeRegex)
    .join("|");

  if (!pattern) {
    return null;
  }

  const matcher = new RegExp(`(${pattern})`, "giu");

  if (!matcher.test(title)) {
    return null;
  }

  matcher.lastIndex = 0;
  const parts: string[] = [];
  let lastIndex = 0;

  for (const match of title.matchAll(matcher)) {
    const start = match.index ?? 0;
    const value = match[0];

    parts.push(escapeHtml(title.slice(lastIndex, start)));
    parts.push(`<mark>${escapeHtml(value)}</mark>`);

    lastIndex = start + value.length;
  }

  parts.push(escapeHtml(title.slice(lastIndex)));

  return parts.join("");
}

// Для preview-сниппета в карточке КБ (под заголовком). В отличие от
// HIGHLIGHT_PRIORITY (title+description для палитры Ctrl+K), здесь предпочитаем
// `content` — у материалов description=null, и текстовое совпадение почти всегда
// прилетает в теле markdown'а (`content`). Локированным документам backend сам
// вырезает content-highlight (FilterHighlights), так что тело не утекает.
const PREVIEW_HIGHLIGHT_PRIORITY = ["content", "description"] as const;

/**
 * Лучший body-сниппет для preview-строки карточки: сперва `content`, затем
 * `description`. Заголовок намеренно не берём — он рендерится отдельно.
 * Возвращает уже нормализованный HTML с &lt;mark&gt; (санитайзится в
 * <see cref="SearchHighlightedMarkdown"/>), либо null.
 */
export function pickSearchPreviewSnippet(highlights: SearchHighlight[]): string | null {
  for (const field of PREVIEW_HIGHLIGHT_PRIORITY) {
    const match = highlights.find(
      (highlight) => highlight.field === field && normalizeSnippet(highlight.snippet).length > 0,
    );
    if (match) {
      return normalizeSnippet(match.snippet);
    }
  }
  return null;
}

export function pickSearchBodyHighlights(highlights: SearchHighlight[], limit = 2) {
  const snippets: string[] = [];
  const seen = new Set<string>();

  for (const field of HIGHLIGHT_PRIORITY) {
    for (const highlight of highlights) {
      if (highlight.field !== field || field === "title") {
        continue;
      }

      const snippet = normalizeSnippet(highlight.snippet);
      if (!snippet || seen.has(snippet)) {
        continue;
      }

      seen.add(snippet);
      snippets.push(snippet);

      if (snippets.length >= limit) {
        return snippets;
      }
    }
  }

  return snippets;
}
