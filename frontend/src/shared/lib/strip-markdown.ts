/**
 * Strips markdown formatting from text to produce a plain-text preview.
 * Handles bold, italic, strikethrough, headings, links, images, code blocks, lists.
 */
export function stripMarkdown(md: string): string {
  return (
    md
      // Remove code blocks (```...```)
      .replace(/```[\s\S]*?```/g, "")
      // Remove inline code (`...`)
      .replace(/`([^`]*)`/g, "$1")
      // Remove images ![alt](url)
      .replace(/!\[([^\]]*)\]\([^)]*\)/g, "$1")
      // Remove links [text](url)
      .replace(/\[([^\]]*)\]\([^)]*\)/g, "$1")
      // Remove headings (## ...)
      .replace(/^#{1,6}\s+/gm, "")
      // Remove bold/italic (**, __, *, _)
      .replace(/\*{1,3}([^*]+)\*{1,3}/g, "$1")
      .replace(/_{1,3}([^_]+)_{1,3}/g, "$1")
      // Remove strikethrough (~~...~~)
      .replace(/~~([^~]+)~~/g, "$1")
      // Remove blockquotes (> ...)
      .replace(/^>\s+/gm, "")
      // Remove horizontal rules (---, ***, ___)
      .replace(/^[-*_]{3,}\s*$/gm, "")
      // Remove list markers (-, *, 1.)
      .replace(/^[\s]*[-*+]\s+/gm, "")
      .replace(/^[\s]*\d+\.\s+/gm, "")
      // Collapse whitespace
      .replace(/\n{2,}/g, " ")
      .replace(/\n/g, " ")
      .trim()
  );
}
