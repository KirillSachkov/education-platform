import { MarkdownContent } from "@/shared/ui/components/markdown-content";

export function SearchHighlightedMarkdown({
  children,
  className,
}: {
  children: string;
  className: string;
}) {
  return (
    <MarkdownContent
      variant="compact"
      disableLinks
      className={`${className} [&_mark]:bg-primary/10 [&_mark]:font-semibold [&_mark]:text-primary [&_mark]:rounded-sm [&_mark]:px-0.5`}
    >
      {children}
    </MarkdownContent>
  );
}
