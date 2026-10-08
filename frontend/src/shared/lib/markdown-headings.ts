export interface MarkdownHeading {
  id: string;
  level: number;
  text: string;
}

function stripMarkdownHeadingText(value: string): string {
  return value
    .replace(/!\[([^\]]*)\]\([^)]+\)/g, "$1")
    .replace(/\[([^\]]+)\]\([^)]+\)/g, "$1")
    .replace(/[*_~`>#]/g, "")
    .replace(/\s+/g, " ")
    .trim();
}

function slugifyHeading(value: string): string {
  const normalized = value
    .normalize("NFKD")
    .toLowerCase()
    .replace(/[^\p{L}\p{N}\s-]/gu, "")
    .trim()
    .replace(/[\s_-]+/g, "-")
    .replace(/^-+|-+$/g, "");

  return normalized || "section";
}

export function createHeadingSlugger() {
  const counts = new Map<string, number>();

  return {
    next(value: string) {
      const base = slugifyHeading(value);
      const current = counts.get(base) ?? 0;
      counts.set(base, current + 1);

      return current === 0 ? base : `${base}-${current + 1}`;
    },
  };
}

export function extractMarkdownHeadings(
  markdown: string,
  {
    minLevel = 1,
    maxLevel = 3,
  }: {
    minLevel?: number;
    maxLevel?: number;
  } = {},
): MarkdownHeading[] {
  const lines = markdown.split(/\r?\n/);
  const slugger = createHeadingSlugger();
  const headings: MarkdownHeading[] = [];
  let inCodeFence = false;

  for (const line of lines) {
    if (/^\s*(```|~~~)/.test(line)) {
      inCodeFence = !inCodeFence;
      continue;
    }

    if (inCodeFence) {
      continue;
    }

    const match = line.match(/^\s{0,3}(#{1,6})\s+(.+?)\s*#*\s*$/);
    if (!match) {
      continue;
    }

    const level = match[1].length;
    if (level < minLevel || level > maxLevel) {
      continue;
    }

    const text = stripMarkdownHeadingText(match[2]);
    if (!text) {
      continue;
    }

    headings.push({
      id: slugger.next(text),
      level,
      text,
    });
  }

  return headings;
}
