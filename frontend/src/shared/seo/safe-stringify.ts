/**
 * JSON-stringify a schema object for inline `<script type=\"application/ld+json\">` tags
 * with proper HTML-context escapes.
 *
 * JSON.stringify does NOT escape </script>, <!--, or ]]> sequences inside strings.
 * Untrusted input (course titles, descriptions, FAQ answers) could close the script tag
 * and inject arbitrary HTML => stored XSS. Standard mitigation is to escape the `<` at
 * positions where it starts one of those sequences. We also escape U+2028 / U+2029
 * (LINE SEPARATOR / PARAGRAPH SEPARATOR), which JSON.stringify leaves raw but JavaScript
 * parsers treat as newlines.
 */
export function safeJsonLdStringify(schema: unknown): string {
  return JSON.stringify(schema)
    .replace(/</g, "\\u003C")
    .replace(/\u2028/g, "\\u2028")
    .replace(/\u2029/g, "\\u2029");
}
